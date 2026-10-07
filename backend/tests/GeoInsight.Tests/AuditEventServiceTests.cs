using GeoInsight.Application.Audit;
using GeoInsight.Application.Common;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Audit;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Organizations;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Tests;

public class AuditEventServiceTests
{
    private sealed class Harness
    {
        public required GeoInsightDbContext DbContext { get; init; }
        public required AuditEventService Service { get; init; }

        public static Harness Create()
        {
            var options = new DbContextOptionsBuilder<GeoInsightDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new GeoInsightDbContext(options);
            var orgAccessControl = new OrganizationAccessControl(dbContext);

            return new Harness
            {
                DbContext = dbContext,
                Service = new AuditEventService(dbContext, orgAccessControl),
            };
        }

        public async Task<ApplicationUser> AddUserAsync(string email)
        {
            var user = new ApplicationUser { UserName = email, Email = email, DisplayName = email };
            DbContext.Users.Add(user);
            await DbContext.SaveChangesAsync();
            return user;
        }

        public async Task<Organization> AddOrgAsync(ApplicationUser owner, OrganizationRole role)
        {
            var org = new Organization { Name = "Acme", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            org.Members.Add(new OrganizationUser { UserId = owner.Id, Role = role, CreatedAt = DateTime.UtcNow });
            DbContext.Organizations.Add(org);
            await DbContext.SaveChangesAsync();
            return org;
        }

        public async Task AddMemberAsync(Organization org, ApplicationUser user, OrganizationRole role)
        {
            DbContext.OrganizationUsers.Add(new OrganizationUser
            {
                OrganizationId = org.Id, UserId = user.Id, Role = role, CreatedAt = DateTime.UtcNow,
            });
            await DbContext.SaveChangesAsync();
        }

        public async Task AddEventAsync(long organizationId, long? userId, string action, DateTime createdAt)
        {
            DbContext.AuditEvents.Add(new AuditEvent
            {
                OrganizationId = organizationId, UserId = userId, Action = action, CreatedAt = createdAt,
            });
            await DbContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task An_administrator_sees_events_newest_first_with_resolved_user_names()
    {
        var h = Harness.Create();
        var admin = await h.AddUserAsync("admin@example.com");
        var org = await h.AddOrgAsync(admin, OrganizationRole.Administrator);
        await h.AddEventAsync(org.Id, admin.Id, "dataset.create", DateTime.UtcNow.AddMinutes(-10));
        await h.AddEventAsync(org.Id, admin.Id, "export.create", DateTime.UtcNow.AddMinutes(-1));

        var result = await h.Service.GetPagedAsync(org.Id, admin.Id, 1, 50, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal("export.create", result.Value.Items[0].Action); // newest first
        Assert.Equal("dataset.create", result.Value.Items[1].Action);
        Assert.All(result.Value.Items, e => Assert.Equal("admin@example.com", e.UserDisplayName));
    }

    [Fact]
    public async Task An_event_with_no_user_resolves_to_a_null_display_name()
    {
        var h = Harness.Create();
        var admin = await h.AddUserAsync("admin@example.com");
        var org = await h.AddOrgAsync(admin, OrganizationRole.Administrator);
        await h.AddEventAsync(org.Id, null, "system.something", DateTime.UtcNow);

        var result = await h.Service.GetPagedAsync(org.Id, admin.Id, 1, 50, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(Assert.Single(result.Value!.Items).UserDisplayName);
    }

    [Fact]
    public async Task Pagination_respects_page_size()
    {
        var h = Harness.Create();
        var admin = await h.AddUserAsync("admin@example.com");
        var org = await h.AddOrgAsync(admin, OrganizationRole.Administrator);
        for (var i = 0; i < 5; i++)
        {
            await h.AddEventAsync(org.Id, admin.Id, $"action.{i}", DateTime.UtcNow.AddMinutes(i));
        }

        var page1 = await h.Service.GetPagedAsync(org.Id, admin.Id, 1, 2, CancellationToken.None);
        var page2 = await h.Service.GetPagedAsync(org.Id, admin.Id, 2, 2, CancellationToken.None);

        Assert.Equal(5, page1.Value!.TotalCount);
        Assert.Equal(2, page1.Value.Items.Count);
        Assert.Equal(2, page2.Value!.Items.Count);
        Assert.NotEqual(page1.Value.Items[0].Action, page2.Value.Items[0].Action);
    }

    [Fact]
    public async Task An_analyst_and_a_viewer_are_both_forbidden()
    {
        var h = Harness.Create();
        var admin = await h.AddUserAsync("admin@example.com");
        var org = await h.AddOrgAsync(admin, OrganizationRole.Administrator);
        var analyst = await h.AddUserAsync("analyst@example.com");
        await h.AddMemberAsync(org, analyst, OrganizationRole.Analyst);
        var viewer = await h.AddUserAsync("viewer@example.com");
        await h.AddMemberAsync(org, viewer, OrganizationRole.Viewer);

        var analystResult = await h.Service.GetPagedAsync(org.Id, analyst.Id, 1, 50, CancellationToken.None);
        var viewerResult = await h.Service.GetPagedAsync(org.Id, viewer.Id, 1, 50, CancellationToken.None);

        Assert.False(analystResult.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, analystResult.Error!.Type);
        Assert.False(viewerResult.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, viewerResult.Error!.Type);
    }

    [Fact]
    public async Task A_non_member_gets_forbidden_and_a_missing_org_gets_not_found()
    {
        var h = Harness.Create();
        var admin = await h.AddUserAsync("admin@example.com");
        var org = await h.AddOrgAsync(admin, OrganizationRole.Administrator);
        var outsider = await h.AddUserAsync("outsider@example.com");

        var notMemberResult = await h.Service.GetPagedAsync(org.Id, outsider.Id, 1, 50, CancellationToken.None);
        var missingOrgResult = await h.Service.GetPagedAsync(999, admin.Id, 1, 50, CancellationToken.None);

        Assert.False(notMemberResult.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, notMemberResult.Error!.Type);
        Assert.False(missingOrgResult.Succeeded);
        Assert.Equal(ServiceErrorType.NotFound, missingOrgResult.Error!.Type);
    }
}
