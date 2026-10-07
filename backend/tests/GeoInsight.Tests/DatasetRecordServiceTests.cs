using GeoInsight.Application.Common;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Datasets;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Organizations;
using GeoInsight.Infrastructure.Persistence;
using GeoInsight.Infrastructure.Records;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Tests;

public class DatasetRecordServiceTests
{
    private sealed class Harness
    {
        public required GeoInsightDbContext DbContext { get; init; }
        public required DatasetRecordService Service { get; init; }

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
                Service = new DatasetRecordService(dbContext, datasetAccessControl),
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

        public async Task AddRecordAsync(
            long datasetId, string addressLine1, decimal? latitude, decimal? longitude, decimal? amount = null,
            string? category = null)
        {
            DbContext.DatasetRecords.Add(new DatasetRecord
            {
                DatasetId = datasetId,
                AddressLine1 = addressLine1,
                City = "Ogden",
                State = "UT",
                Latitude = latitude,
                Longitude = longitude,
                Amount = amount,
                Category = category,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await DbContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Points_only_includes_records_with_both_coordinates()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("user@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddRecordAsync(dataset.Id, "100 Main St", 41.22m, -111.97m);
        await h.AddRecordAsync(dataset.Id, "200 Oak Ave", null, null);

        var result = await h.Service.GetPointsAsync(org.Id, dataset.Id, user.Id, new RecordFilterRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Value!);
        Assert.Equal(41.22m, result.Value![0].Latitude);
        Assert.Equal(-111.97m, result.Value![0].Longitude);
    }

    [Fact]
    public async Task Paging_returns_the_correct_page_and_total_count()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("user@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        for (var i = 0; i < 5; i++)
        {
            await h.AddRecordAsync(dataset.Id, $"Address {i}", null, null);
        }

        var result = await h.Service.GetPagedAsync(
            org.Id, dataset.Id, user.Id, page: 2, pageSize: 2, null, null, new RecordFilterRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.Value!.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal(2, result.Value.Page);
    }

    [Fact]
    public async Task An_unknown_sortBy_falls_back_to_createdAt_instead_of_erroring()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("user@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddRecordAsync(dataset.Id, "100 Main St", null, null);

        var result = await h.Service.GetPagedAsync(
            org.Id, dataset.Id, user.Id, page: 1, pageSize: 10, "'; DROP TABLE dataset_records; --", null,
            new RecordFilterRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task Sorting_by_amount_ascending_orders_correctly()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("user@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddRecordAsync(dataset.Id, "High", null, null, 500);
        await h.AddRecordAsync(dataset.Id, "Low", null, null, 100);
        await h.AddRecordAsync(dataset.Id, "Mid", null, null, 300);

        var result = await h.Service.GetPagedAsync(
            org.Id, dataset.Id, user.Id, page: 1, pageSize: 10, "amount", "asc", new RecordFilterRequest(), CancellationToken.None);

        Assert.Equal(["Low", "Mid", "High"], result.Value!.Items.Select(i => i.AddressLine1));
    }

    [Fact]
    public async Task A_dataset_from_another_organization_is_not_found()
    {
        var h = Harness.Create();
        var userA = await h.AddUserAsync("a@example.com");
        var userB = await h.AddUserAsync("b@example.com");
        var (_, datasetA) = await h.AddOrgAndDatasetAsync(userA, OrganizationRole.Administrator);
        var (orgB, _) = await h.AddOrgAndDatasetAsync(userB, OrganizationRole.Administrator);

        var result = await h.Service.GetPointsAsync(orgB.Id, datasetA.Id, userB.Id, new RecordFilterRequest(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task Filtering_by_category_narrows_both_points_and_paged_results_the_same_way()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("user@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddRecordAsync(dataset.Id, "Mortgage record", 41.22m, -111.97m, category: "Mortgage");
        await h.AddRecordAsync(dataset.Id, "Auto record", 41.23m, -111.98m, category: "Auto Loan");

        var filter = new RecordFilterRequest(Category: "Mortgage");
        var points = await h.Service.GetPointsAsync(org.Id, dataset.Id, user.Id, filter, CancellationToken.None);
        var paged = await h.Service.GetPagedAsync(org.Id, dataset.Id, user.Id, 1, 10, null, null, filter, CancellationToken.None);

        Assert.Single(points.Value!);
        Assert.Equal(1, paged.Value!.TotalCount);
        Assert.Equal("Mortgage record", paged.Value.Items[0].AddressLine1);
    }

    [Fact]
    public async Task Filtering_by_amount_range_excludes_records_outside_it()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("user@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddRecordAsync(dataset.Id, "Low", null, null, amount: 100);
        await h.AddRecordAsync(dataset.Id, "Mid", null, null, amount: 300);
        await h.AddRecordAsync(dataset.Id, "High", null, null, amount: 500);

        var filter = new RecordFilterRequest(MinAmount: 200, MaxAmount: 400);
        var result = await h.Service.GetPagedAsync(org.Id, dataset.Id, user.Id, 1, 10, null, null, filter, CancellationToken.None);

        Assert.Equal(["Mid"], result.Value!.Items.Select(i => i.AddressLine1));
    }

    [Fact]
    public async Task Pagination_total_count_reflects_the_filter_not_the_whole_dataset()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("user@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddRecordAsync(dataset.Id, "A", null, null, category: "Keep");
        await h.AddRecordAsync(dataset.Id, "B", null, null, category: "Keep");
        await h.AddRecordAsync(dataset.Id, "C", null, null, category: "Drop");

        var filter = new RecordFilterRequest(Category: "Keep");
        var result = await h.Service.GetPagedAsync(org.Id, dataset.Id, user.Id, 1, 10, null, null, filter, CancellationToken.None);

        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);
    }
}
