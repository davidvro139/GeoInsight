using GeoInsight.Application.Common;
using GeoInsight.Application.HmdaBenchmark;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Datasets;
using GeoInsight.Infrastructure.HmdaBenchmark;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Organizations;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Tests;

/// <summary>
/// Uses a fake IHmdaBenchmarkSource (no live network call) to exercise the
/// service's own logic: per-county grouping/aggregation, share-% math, and
/// graceful handling of a county the real CFPB API has no data for — the
/// real HTTP-backed HmdaBenchmarkSource is exercised separately, live,
/// during this feature's manual verification (see TODO.md).
/// </summary>
public class HmdaBenchmarkServiceTests
{
    private sealed class FakeHmdaBenchmarkSource(Func<string, int, HmdaCountyAggregate?> respond) : IHmdaBenchmarkSource
    {
        public List<(string CountyFips, int Year)> Calls { get; } = [];

        public Task<HmdaCountyAggregate?> GetOriginatedLoanAggregateAsync(string countyFips, int year, CancellationToken cancellationToken)
        {
            Calls.Add((countyFips, year));
            return Task.FromResult(respond(countyFips, year));
        }
    }

    private sealed class Harness
    {
        public required GeoInsightDbContext DbContext { get; init; }
        public required HmdaBenchmarkService Service { get; init; }
        public required FakeHmdaBenchmarkSource Source { get; init; }

        public static Harness Create(Func<string, int, HmdaCountyAggregate?>? respond = null)
        {
            var options = new DbContextOptionsBuilder<GeoInsightDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new GeoInsightDbContext(options);
            var orgAccessControl = new OrganizationAccessControl(dbContext);
            var datasetAccessControl = new DatasetAccessControl(dbContext, orgAccessControl);
            var source = new FakeHmdaBenchmarkSource(respond ?? ((_, _) => null));

            return new Harness
            {
                DbContext = dbContext,
                Source = source,
                Service = new HmdaBenchmarkService(dbContext, datasetAccessControl, source),
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
            long datasetId, decimal? amount = null, string? stateFips = null, string? countyFips = null, string? category = null)
        {
            DbContext.DatasetRecords.Add(new DatasetRecord
            {
                DatasetId = datasetId, AddressLine1 = "100 Main St", City = "Ogden", State = "UT",
                GeocodeStatus = "matched", Amount = amount, Category = category,
                CensusStateFips = stateFips, CensusCountyFips = countyFips,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await DbContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task A_viewer_can_load_the_benchmark()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);

        var result = await h.Service.GetBenchmarkAsync(org.Id, dataset.Id, user.Id, 2023, new RecordFilterRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_dataset_from_another_organization_is_not_found()
    {
        var h = Harness.Create();
        var userA = await h.AddUserAsync("a@example.com");
        var userB = await h.AddUserAsync("b@example.com");
        var (_, datasetA) = await h.AddOrgAndDatasetAsync(userA, OrganizationRole.Administrator);
        var (orgB, _) = await h.AddOrgAndDatasetAsync(userB, OrganizationRole.Administrator);

        var result = await h.Service.GetBenchmarkAsync(orgB.Id, datasetA.Id, userB.Id, 2023, new RecordFilterRequest(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task Records_are_grouped_by_full_state_plus_county_fips_and_compared_against_the_market()
    {
        var h = Harness.Create((countyFips, year) =>
            countyFips == "49057" && year == 2023 ? new HmdaCountyAggregate(7806, 1_952_660_000m) : null);
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddRecordAsync(dataset.Id, amount: 300_000, stateFips: "49", countyFips: "057");
        await h.AddRecordAsync(dataset.Id, amount: 250_000, stateFips: "49", countyFips: "057");

        var result = await h.Service.GetBenchmarkAsync(org.Id, dataset.Id, user.Id, 2023, new RecordFilterRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var row = Assert.Single(result.Value!);
        Assert.Equal("49", row.StateFips);
        Assert.Equal("057", row.CountyFips);
        Assert.Equal(2, row.DatasetRecordCount);
        Assert.Equal(550_000, row.DatasetTotalAmount);
        Assert.Equal(7806, row.MarketOriginatedLoanCount);
        Assert.Equal(1_952_660_000m, row.MarketTotalAmount);
        Assert.Equal(Math.Round(2m / 7806 * 100, 2), row.DatasetSharePercent);
        Assert.Contains(("49057", 2023), h.Source.Calls);
    }

    [Fact]
    public async Task A_county_the_source_has_no_data_for_reports_null_market_fields_not_a_zero()
    {
        var h = Harness.Create((_, _) => null);
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddRecordAsync(dataset.Id, amount: 100_000, stateFips: "49", countyFips: "057");

        var result = await h.Service.GetBenchmarkAsync(org.Id, dataset.Id, user.Id, 2023, new RecordFilterRequest(), CancellationToken.None);

        var row = Assert.Single(result.Value!);
        Assert.Null(row.MarketOriginatedLoanCount);
        Assert.Null(row.MarketTotalAmount);
        Assert.Null(row.DatasetSharePercent);
    }

    [Fact]
    public async Task Records_with_no_assigned_county_are_excluded_not_folded_into_a_fake_bucket()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddRecordAsync(dataset.Id, amount: 100_000); // no state/county fips

        var result = await h.Service.GetBenchmarkAsync(org.Id, dataset.Id, user.Id, 2023, new RecordFilterRequest(), CancellationToken.None);

        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Multiple_counties_produce_one_source_call_each_not_a_combined_call()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddRecordAsync(dataset.Id, amount: 100_000, stateFips: "49", countyFips: "057");
        await h.AddRecordAsync(dataset.Id, amount: 200_000, stateFips: "49", countyFips: "035");

        var result = await h.Service.GetBenchmarkAsync(org.Id, dataset.Id, user.Id, 2023, new RecordFilterRequest(), CancellationToken.None);

        Assert.Equal(2, result.Value!.Count);
        Assert.Equal(2, h.Source.Calls.Count);
        Assert.Contains(("49057", 2023), h.Source.Calls);
        Assert.Contains(("49035", 2023), h.Source.Calls);
    }

    [Fact]
    public async Task The_filter_narrows_which_records_and_counties_are_benchmarked()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddRecordAsync(dataset.Id, amount: 100_000, stateFips: "49", countyFips: "057", category: "Mortgage");
        await h.AddRecordAsync(dataset.Id, amount: 200_000, stateFips: "49", countyFips: "035", category: "Auto");

        var result = await h.Service.GetBenchmarkAsync(
            org.Id, dataset.Id, user.Id, 2023, new RecordFilterRequest(Category: "Mortgage"), CancellationToken.None);

        var row = Assert.Single(result.Value!);
        Assert.Equal("057", row.CountyFips);
    }
}
