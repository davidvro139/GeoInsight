using GeoInsight.Application.Common;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Common;
using GeoInsight.Infrastructure.Datasets;
using GeoInsight.Infrastructure.Geocoding;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Organizations;
using GeoInsight.Infrastructure.Persistence;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Tests;

/// <summary>
/// Uses a real IBackgroundJobClient backed by Hangfire.MemoryStorage (no
/// MySQL needed) — jobs enqueued here just sit unexecuted, which is fine:
/// this tests GeocodeJobService's own logic (permissions, validation,
/// cross-org isolation), not whether a job actually runs to completion —
/// that's GeocodeJobRunnerTests' job.
/// </summary>
public class GeocodeJobServiceTests
{
    private sealed class Harness
    {
        public required GeoInsightDbContext DbContext { get; init; }
        public required GeocodeJobService Service { get; init; }

        private sealed class FakeBackgroundJobClient : IBackgroundJobClient
        {
            public string Create(Job job, IState state) => Guid.NewGuid().ToString("N");

            public bool ChangeState(string jobId, IState state, string expectedState) => true;
        }

        public static Harness Create()
        {
            var options = new DbContextOptionsBuilder<GeoInsightDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new GeoInsightDbContext(options);
            var orgAccessControl = new OrganizationAccessControl(dbContext);
            var datasetAccessControl = new DatasetAccessControl(dbContext, orgAccessControl);
            var backgroundJobClient = new FakeBackgroundJobClient();

            return new Harness
            {
                DbContext = dbContext,
                Service = new GeocodeJobService(dbContext, datasetAccessControl, backgroundJobClient, new AuditService(dbContext)),
            };
        }

        public async Task<ApplicationUser> AddUserAsync(string email)
        {
            var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Test User" };
            DbContext.Users.Add(user);
            await DbContext.SaveChangesAsync();
            return user;
        }

        public async Task<(Organization Org, Dataset Dataset)> AddOrgAndDatasetAsync(ApplicationUser user, OrganizationRole role)
        {
            var org = new Organization { Name = "Acme", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            org.Members.Add(new OrganizationUser { UserId = user.Id, Role = role, CreatedAt = DateTime.UtcNow });
            DbContext.Organizations.Add(org);
            await DbContext.SaveChangesAsync();

            var dataset = new Dataset
            {
                OrganizationId = org.Id, Name = "Loans", CreatedByUserId = user.Id,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            };
            DbContext.Datasets.Add(dataset);
            await DbContext.SaveChangesAsync();

            return (org, dataset);
        }

        public async Task AddPendingRecordAsync(long datasetId)
        {
            DbContext.DatasetRecords.Add(new DatasetRecord
            {
                DatasetId = datasetId,
                AddressLine1 = "100 Main St",
                City = "Ogden",
                State = "UT",
                GeocodeStatus = "pending",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await DbContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task An_analyst_can_start_a_geocode_job_when_there_is_pending_work()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        await h.AddPendingRecordAsync(dataset.Id);

        var result = await h.Service.CreateAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Queued", result.Value!.Status);

        var auditEvent = Assert.Single(h.DbContext.AuditEvents);
        Assert.Equal("geocoding.start", auditEvent.Action);
        Assert.Equal("geocode_job", auditEvent.EntityType);
        Assert.Equal(result.Value.Id.ToString(), auditEvent.EntityId);
    }

    [Fact]
    public async Task Creating_a_job_with_nothing_pending_is_a_validation_error()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        // no pending records added

        var result = await h.Service.CreateAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task A_viewer_cannot_start_a_geocode_job()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddPendingRecordAsync(dataset.Id);

        var result = await h.Service.CreateAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task A_viewer_can_read_job_status()
    {
        var h = Harness.Create();
        var analyst = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(analyst, OrganizationRole.Administrator);
        await h.AddPendingRecordAsync(dataset.Id);
        var created = (await h.Service.CreateAsync(org.Id, dataset.Id, analyst.Id, CancellationToken.None)).Value!;

        var viewer = await h.AddUserAsync("viewer@example.com");
        h.DbContext.OrganizationUsers.Add(new OrganizationUser
        {
            OrganizationId = org.Id, UserId = viewer.Id, Role = OrganizationRole.Viewer, CreatedAt = DateTime.UtcNow,
        });
        await h.DbContext.SaveChangesAsync();

        var result = await h.Service.GetAsync(org.Id, dataset.Id, created.Id, viewer.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Cancelling_a_queued_job_marks_it_cancelled()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        await h.AddPendingRecordAsync(dataset.Id);
        var created = (await h.Service.CreateAsync(org.Id, dataset.Id, user.Id, CancellationToken.None)).Value!;

        var result = await h.Service.CancelAsync(org.Id, dataset.Id, created.Id, user.Id, CancellationToken.None);
        var fetched = await h.Service.GetAsync(org.Id, dataset.Id, created.Id, user.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Cancelled", fetched.Value!.Status);
    }

    [Fact]
    public async Task A_geocode_job_from_another_organization_is_not_found()
    {
        var h = Harness.Create();
        var userA = await h.AddUserAsync("a@example.com");
        var userB = await h.AddUserAsync("b@example.com");
        var (orgA, datasetA) = await h.AddOrgAndDatasetAsync(userA, OrganizationRole.Administrator);
        var (orgB, _) = await h.AddOrgAndDatasetAsync(userB, OrganizationRole.Administrator);
        await h.AddPendingRecordAsync(datasetA.Id);
        var job = (await h.Service.CreateAsync(orgA.Id, datasetA.Id, userA.Id, CancellationToken.None)).Value!;

        var result = await h.Service.GetAsync(orgB.Id, datasetA.Id, job.Id, userB.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.NotFound, result.Error!.Type);
    }
}
