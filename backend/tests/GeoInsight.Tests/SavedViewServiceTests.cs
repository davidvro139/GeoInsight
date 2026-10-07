using GeoInsight.Application.Common;
using GeoInsight.Application.SavedViews;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Common;
using GeoInsight.Infrastructure.Datasets;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Organizations;
using GeoInsight.Infrastructure.Persistence;
using GeoInsight.Infrastructure.SavedViews;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Tests;

public class SavedViewServiceTests
{
    private sealed class Harness
    {
        public required GeoInsightDbContext DbContext { get; init; }
        public required SavedViewService Service { get; init; }

        public static Harness Create()
        {
            var options = new DbContextOptionsBuilder<GeoInsightDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new GeoInsightDbContext(options);
            var orgAccessControl = new OrganizationAccessControl(dbContext);
            var datasetAccessControl = new DatasetAccessControl(dbContext, orgAccessControl);

            return new Harness
            {
                DbContext = dbContext,
                Service = new SavedViewService(dbContext, datasetAccessControl, orgAccessControl, new AuditService(dbContext)),
            };
        }

        public async Task<ApplicationUser> AddUserAsync(string email)
        {
            var user = new ApplicationUser { UserName = email, Email = email, DisplayName = email };
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

        public async Task AddMemberAsync(Organization org, ApplicationUser user, OrganizationRole role)
        {
            DbContext.OrganizationUsers.Add(new OrganizationUser
            {
                OrganizationId = org.Id, UserId = user.Id, Role = role, CreatedAt = DateTime.UtcNow,
            });
            await DbContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Create_round_trips_filters_and_map_configuration_through_json()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("owner@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);

        var filters = new RecordFilterRequest(Category: "Mortgage", MinAmount: 100_000m);
        var mapConfig = new MapConfigurationDto(true, "RecordCount");

        var result = await h.Service.CreateAsync(
            org.Id, dataset.Id, user.Id, new CreateSavedViewRequest("My View", filters, mapConfig), CancellationToken.None);

        Assert.True(result.Succeeded);
        var dto = result.Value!;
        Assert.Equal("My View", dto.Name);
        Assert.Equal(user.Id, dto.UserId);
        Assert.Equal("owner@example.com", dto.OwnerDisplayName);
        Assert.NotNull(dto.Filters);
        Assert.Equal("Mortgage", dto.Filters!.Category);
        Assert.Equal(100_000m, dto.Filters.MinAmount);
        Assert.NotNull(dto.MapConfiguration);
        Assert.True(dto.MapConfiguration!.ShowTracts);
        Assert.Equal("RecordCount", dto.MapConfiguration.ChoroplethMetric);

        var auditEvent = Assert.Single(h.DbContext.AuditEvents);
        Assert.Equal("saved_view.create", auditEvent.Action);
        Assert.Equal("saved_view", auditEvent.EntityType);
        Assert.Equal(dto.Id.ToString(), auditEvent.EntityId);
    }

    [Fact]
    public async Task Create_rejects_a_blank_name()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("owner@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);

        var result = await h.Service.CreateAsync(
            org.Id, dataset.Id, user.Id, new CreateSavedViewRequest("   ", null, null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task GetAll_returns_every_view_for_the_dataset_with_owner_names()
    {
        var h = Harness.Create();
        var owner = await h.AddUserAsync("owner@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(owner, OrganizationRole.Viewer);
        await h.Service.CreateAsync(org.Id, dataset.Id, owner.Id, new CreateSavedViewRequest("View A", null, null), CancellationToken.None);
        await h.Service.CreateAsync(org.Id, dataset.Id, owner.Id, new CreateSavedViewRequest("View B", null, null), CancellationToken.None);

        var result = await h.Service.GetAllAsync(org.Id, dataset.Id, owner.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Count);
        Assert.All(result.Value, v => Assert.Equal("owner@example.com", v.OwnerDisplayName));
    }

    [Fact]
    public async Task Owner_can_rename_and_delete_their_own_view()
    {
        var h = Harness.Create();
        var owner = await h.AddUserAsync("owner@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(owner, OrganizationRole.Viewer);
        var created = await h.Service.CreateAsync(org.Id, dataset.Id, owner.Id, new CreateSavedViewRequest("Original", null, null), CancellationToken.None);
        var viewId = created.Value!.Id;

        var renamed = await h.Service.RenameAsync(org.Id, dataset.Id, viewId, owner.Id, new RenameSavedViewRequest("Renamed"), CancellationToken.None);
        Assert.True(renamed.Succeeded);
        Assert.Equal("Renamed", renamed.Value!.Name);

        var deleted = await h.Service.DeleteAsync(org.Id, dataset.Id, viewId, owner.Id, CancellationToken.None);
        Assert.True(deleted.Succeeded);

        var listAfterDelete = await h.Service.GetAllAsync(org.Id, dataset.Id, owner.Id, CancellationToken.None);
        Assert.Empty(listAfterDelete.Value!);
    }

    [Fact]
    public async Task A_non_owner_non_admin_cannot_rename_or_delete_someone_elses_view()
    {
        var h = Harness.Create();
        var owner = await h.AddUserAsync("owner@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(owner, OrganizationRole.Viewer);
        var otherViewer = await h.AddUserAsync("other@example.com");
        await h.AddMemberAsync(org, otherViewer, OrganizationRole.Analyst);
        var created = await h.Service.CreateAsync(org.Id, dataset.Id, owner.Id, new CreateSavedViewRequest("Original", null, null), CancellationToken.None);
        var viewId = created.Value!.Id;

        var renameResult = await h.Service.RenameAsync(org.Id, dataset.Id, viewId, otherViewer.Id, new RenameSavedViewRequest("Hijacked"), CancellationToken.None);
        Assert.False(renameResult.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, renameResult.Error!.Type);

        var deleteResult = await h.Service.DeleteAsync(org.Id, dataset.Id, viewId, otherViewer.Id, CancellationToken.None);
        Assert.False(deleteResult.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, deleteResult.Error!.Type);
    }

    [Fact]
    public async Task An_administrator_can_rename_and_delete_someone_elses_view()
    {
        var h = Harness.Create();
        var owner = await h.AddUserAsync("owner@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(owner, OrganizationRole.Viewer);
        var admin = await h.AddUserAsync("admin@example.com");
        await h.AddMemberAsync(org, admin, OrganizationRole.Administrator);
        var created = await h.Service.CreateAsync(org.Id, dataset.Id, owner.Id, new CreateSavedViewRequest("Original", null, null), CancellationToken.None);
        var viewId = created.Value!.Id;

        var renameResult = await h.Service.RenameAsync(org.Id, dataset.Id, viewId, admin.Id, new RenameSavedViewRequest("Cleaned up"), CancellationToken.None);
        Assert.True(renameResult.Succeeded);
        Assert.Equal("Cleaned up", renameResult.Value!.Name);

        var deleteResult = await h.Service.DeleteAsync(org.Id, dataset.Id, viewId, admin.Id, CancellationToken.None);
        Assert.True(deleteResult.Succeeded);
    }

    [Fact]
    public async Task A_view_from_another_organization_is_not_found()
    {
        var h = Harness.Create();
        var userA = await h.AddUserAsync("a@example.com");
        var userB = await h.AddUserAsync("b@example.com");
        var (orgA, datasetA) = await h.AddOrgAndDatasetAsync(userA, OrganizationRole.Administrator);
        var (orgB, _) = await h.AddOrgAndDatasetAsync(userB, OrganizationRole.Administrator);
        var created = await h.Service.CreateAsync(orgA.Id, datasetA.Id, userA.Id, new CreateSavedViewRequest("Original", null, null), CancellationToken.None);

        var renameResult = await h.Service.RenameAsync(orgB.Id, datasetA.Id, created.Value!.Id, userB.Id, new RenameSavedViewRequest("Hijacked"), CancellationToken.None);

        Assert.False(renameResult.Succeeded);
        Assert.Equal(ServiceErrorType.NotFound, renameResult.Error!.Type);
    }

    [Fact]
    public async Task A_viewer_can_create_and_list_their_own_view_with_no_analyst_requirement()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);

        var createResult = await h.Service.CreateAsync(org.Id, dataset.Id, user.Id, new CreateSavedViewRequest("Mine", null, null), CancellationToken.None);
        Assert.True(createResult.Succeeded);

        var listResult = await h.Service.GetAllAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);
        Assert.True(listResult.Succeeded);
        Assert.Single(listResult.Value!);
    }
}
