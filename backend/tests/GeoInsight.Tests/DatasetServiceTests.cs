using GeoInsight.Application.Common;
using GeoInsight.Application.Datasets;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Common;
using GeoInsight.Infrastructure.Datasets;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Organizations;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Tests;

/// <summary>
/// Unlike OrganizationServiceTests, this doesn't need a full Identity
/// UserManager harness — DatasetService only reads dbContext.Users for the
/// created-by join, so ApplicationUser rows are seeded directly.
/// </summary>
public class DatasetServiceTests
{
    private sealed class Harness
    {
        public required GeoInsightDbContext DbContext { get; init; }
        public required DatasetService Service { get; init; }

        public static Harness Create()
        {
            var options = new DbContextOptionsBuilder<GeoInsightDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new GeoInsightDbContext(options);

            return new Harness
            {
                DbContext = dbContext,
                Service = new DatasetService(dbContext, new OrganizationAccessControl(dbContext), new AuditService(dbContext)),
            };
        }

        public async Task<ApplicationUser> AddUserAsync(string email, string displayName)
        {
            var user = new ApplicationUser { UserName = email, Email = email, DisplayName = displayName };
            DbContext.Users.Add(user);
            await DbContext.SaveChangesAsync();
            return user;
        }

        public async Task<Organization> AddOrganizationWithMemberAsync(string name, ApplicationUser owner, OrganizationRole role)
        {
            var organization = new Organization { Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            organization.Members.Add(new OrganizationUser { UserId = owner.Id, Role = role, CreatedAt = DateTime.UtcNow });
            DbContext.Organizations.Add(organization);
            await DbContext.SaveChangesAsync();
            return organization;
        }
    }

    [Fact]
    public async Task An_analyst_can_create_a_dataset()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("analyst@example.com", "Analyst");
        var org = await h.AddOrganizationWithMemberAsync("Acme", user, OrganizationRole.Analyst);

        var result = await h.Service.CreateAsync(
            org.Id, user.Id, new CreateDatasetRequest("2026 Mortgage Loans", null, null), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("2026 Mortgage Loans", result.Value!.Name);
        Assert.Equal("generic", result.Value.DatasetType);
        Assert.Equal("Analyst", result.Value.CreatedByDisplayName);

        var auditEvent = Assert.Single(h.DbContext.AuditEvents);
        Assert.Equal("dataset.create", auditEvent.Action);
        Assert.Equal("dataset", auditEvent.EntityType);
        Assert.Equal(result.Value.Id.ToString(), auditEvent.EntityId);
    }

    [Fact]
    public async Task A_viewer_cannot_create_a_dataset()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com", "Viewer");
        var org = await h.AddOrganizationWithMemberAsync("Acme", user, OrganizationRole.Viewer);

        var result = await h.Service.CreateAsync(
            org.Id, user.Id, new CreateDatasetRequest("Branch Locations", null, "branch"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task A_non_member_cannot_create_a_dataset()
    {
        var h = Harness.Create();
        var owner = await h.AddUserAsync("owner@example.com", "Owner");
        var outsider = await h.AddUserAsync("outsider@example.com", "Outsider");
        var org = await h.AddOrganizationWithMemberAsync("Acme", owner, OrganizationRole.Administrator);

        var result = await h.Service.CreateAsync(
            org.Id, outsider.Id, new CreateDatasetRequest("Complaints", null, null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task Listing_excludes_archived_datasets_unless_asked_for()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("admin@example.com", "Admin");
        var org = await h.AddOrganizationWithMemberAsync("Acme", user, OrganizationRole.Administrator);
        var dataset = (await h.Service.CreateAsync(org.Id, user.Id, new CreateDatasetRequest("ATMs", null, "atm"), CancellationToken.None)).Value!;
        await h.Service.ArchiveAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);

        var defaultList = await h.Service.GetAllAsync(org.Id, user.Id, includeArchived: false, CancellationToken.None);
        var fullList = await h.Service.GetAllAsync(org.Id, user.Id, includeArchived: true, CancellationToken.None);

        Assert.Empty(defaultList.Value!);
        Assert.Single(fullList.Value!);
        Assert.True(fullList.Value![0].IsArchived);
    }

    [Fact]
    public async Task Archive_then_unarchive_round_trips()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("admin@example.com", "Admin");
        var org = await h.AddOrganizationWithMemberAsync("Acme", user, OrganizationRole.Administrator);
        var dataset = (await h.Service.CreateAsync(org.Id, user.Id, new CreateDatasetRequest("Investments", null, null), CancellationToken.None)).Value!;

        await h.Service.ArchiveAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);
        var afterArchive = await h.Service.GetByIdAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);

        await h.Service.UnarchiveAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);
        var afterUnarchive = await h.Service.GetByIdAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);

        Assert.True(afterArchive.Value!.IsArchived);
        Assert.False(afterUnarchive.Value!.IsArchived);
    }

    [Fact]
    public async Task A_dataset_from_another_organization_is_not_found()
    {
        var h = Harness.Create();
        var adminA = await h.AddUserAsync("admin-a@example.com", "Admin A");
        var adminB = await h.AddUserAsync("admin-b@example.com", "Admin B");
        var orgA = await h.AddOrganizationWithMemberAsync("Org A", adminA, OrganizationRole.Administrator);
        var orgB = await h.AddOrganizationWithMemberAsync("Org B", adminB, OrganizationRole.Administrator);
        var dataset = (await h.Service.CreateAsync(orgA.Id, adminA.Id, new CreateDatasetRequest("Org A Data", null, null), CancellationToken.None)).Value!;

        // adminB is a legitimate Administrator, just of the wrong organization —
        // this must 404, not merely 403, since the dataset isn't even in org B.
        var result = await h.Service.GetByIdAsync(orgB.Id, dataset.Id, adminB.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.NotFound, result.Error!.Type);
    }
}
