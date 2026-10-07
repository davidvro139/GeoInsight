using GeoInsight.Application.Common;
using GeoInsight.Application.Tracts;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Datasets;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Organizations;
using GeoInsight.Infrastructure.Persistence;
using GeoInsight.Infrastructure.Tracts;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GeoInsight.Tests;

/// <summary>
/// Uses a real IBackgroundJobClient backed by Hangfire.MemoryStorage (no
/// MySQL needed) — jobs enqueued here just sit unexecuted, matching
/// GeocodeJobServiceTests' approach: this tests permissions/status logic,
/// not whether an import actually runs (TractImportRunnerTests' job).
/// </summary>
public class CensusTractImportServiceTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    private sealed class Harness
    {
        public required GeoInsightDbContext DbContext { get; init; }
        public required CensusTractImportService Service { get; init; }

        public static Harness Create()
        {
            var options = new DbContextOptionsBuilder<GeoInsightDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new GeoInsightDbContext(options);
            var orgAccessControl = new OrganizationAccessControl(dbContext);
            var datasetAccessControl = new DatasetAccessControl(dbContext, orgAccessControl);
            var backgroundJobClient = new BackgroundJobClient(new MemoryStorage());

            return new Harness
            {
                DbContext = dbContext,
                Service = new CensusTractImportService(dbContext, datasetAccessControl, orgAccessControl, backgroundJobClient),
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

        public async Task AddGeocodedRecordAsync(long datasetId, string stateFips, string countyFips)
        {
            DbContext.DatasetRecords.Add(new DatasetRecord
            {
                DatasetId = datasetId, AddressLine1 = "100 Main St", City = "Ogden", State = "UT",
                GeocodeStatus = "matched", CensusStateFips = stateFips, CensusCountyFips = countyFips,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await DbContext.SaveChangesAsync();
        }

        public async Task AddImportedTractAsync(string stateFips, string countyFips, string tractCode = "000100")
        {
            var square = Factory.CreatePolygon([
                new Coordinate(0, 0), new Coordinate(1, 0), new Coordinate(1, 1), new Coordinate(0, 1), new Coordinate(0, 0),
            ]);
            DbContext.CensusTracts.Add(new CensusTract
            {
                Geoid = $"{stateFips}{countyFips}{tractCode}", StateFips = stateFips, CountyFips = countyFips,
                TractCode = tractCode, Boundary = Factory.CreateMultiPolygon([square]),
            });
            await DbContext.SaveChangesAsync();
        }

        public async Task AddDemographicsAsync(string geoid, int? population = null, decimal? medianHouseholdIncome = null)
        {
            DbContext.CensusDemographics.Add(new CensusDemographic
            {
                CensusGeoid = geoid, SurveyYear = 2022, Population = population, MedianHouseholdIncome = medianHouseholdIncome,
            });
            await DbContext.SaveChangesAsync();
        }

        public async Task AddIncomeLevelAsync(string geoid, TractIncomeTier tier, decimal? tractMfi = null, decimal? areaMfi = null)
        {
            DbContext.CensusTractIncomeLevels.Add(new CensusTractIncomeLevel
            {
                CensusGeoid = geoid, SurveyPeriod = "2024-2026", IncomeTier = tier,
                TractMedianFamilyIncome = tractMfi, AreaMedianFamilyIncome = areaMfi,
            });
            await DbContext.SaveChangesAsync();
        }

        public async Task AddRecordWithGeoidAsync(
            long datasetId, string stateFips, string countyFips, string geoid, decimal? amount = null, string? category = null)
        {
            DbContext.DatasetRecords.Add(new DatasetRecord
            {
                DatasetId = datasetId, AddressLine1 = "100 Main St", City = "Ogden", State = "UT",
                GeocodeStatus = "matched", CensusStateFips = stateFips, CensusCountyFips = countyFips, CensusGeoid = geoid,
                Amount = amount, Category = category,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await DbContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Status_reports_needed_counties_when_nothing_is_imported_yet()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddGeocodedRecordAsync(dataset.Id, "49", "057");

        var result = await h.Service.GetStatusAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.AllImported);
        Assert.Single(result.Value.NeededCounties);
        Assert.Empty(result.Value.ImportedCounties);
    }

    [Fact]
    public async Task Status_reports_all_imported_once_tracts_exist_for_every_needed_county()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddGeocodedRecordAsync(dataset.Id, "49", "057");
        await h.AddImportedTractAsync("49", "057");

        var result = await h.Service.GetStatusAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);

        Assert.True(result.Value!.AllImported);
        Assert.Single(result.Value.ImportedCounties);
    }

    [Fact]
    public async Task A_viewer_cannot_request_an_import()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddGeocodedRecordAsync(dataset.Id, "49", "057");

        var result = await h.Service.RequestImportAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task An_analyst_can_request_an_import()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        await h.AddGeocodedRecordAsync(dataset.Id, "49", "057");

        var result = await h.Service.RequestImportAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);

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

        var result = await h.Service.GetStatusAsync(orgB.Id, datasetA.Id, userB.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task Demographic_metrics_come_straight_from_census_demographics_regardless_of_filter()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddGeocodedRecordAsync(dataset.Id, "49", "057");
        await h.AddImportedTractAsync("49", "057");
        await h.AddDemographicsAsync("49057000100", population: 4200);

        // A filter that matches nothing among the records should have zero effect on a demographic metric.
        var filter = new RecordFilterRequest(Category: "does-not-exist");
        var result = await h.Service.GetChoroplethAsync(
            org.Id, dataset.Id, user.Id, ChoroplethMetric.Population, filter, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Value!);
        Assert.Equal(4200, result.Value![0].Value);
    }

    [Fact]
    public async Task Record_count_respects_the_active_filter()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddImportedTractAsync("49", "057");
        await h.AddRecordWithGeoidAsync(dataset.Id, "49", "057", "49057000100", category: "Mortgage");
        await h.AddRecordWithGeoidAsync(dataset.Id, "49", "057", "49057000100", category: "Auto");

        var unfiltered = await h.Service.GetChoroplethAsync(
            org.Id, dataset.Id, user.Id, ChoroplethMetric.RecordCount, new RecordFilterRequest(), CancellationToken.None);
        var filtered = await h.Service.GetChoroplethAsync(
            org.Id, dataset.Id, user.Id, ChoroplethMetric.RecordCount, new RecordFilterRequest(Category: "Mortgage"), CancellationToken.None);

        Assert.Equal(2, unfiltered.Value![0].Value);
        Assert.Equal(1, filtered.Value![0].Value);
    }

    [Fact]
    public async Task Total_and_average_amount_are_computed_from_the_filtered_records()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddImportedTractAsync("49", "057");
        await h.AddRecordWithGeoidAsync(dataset.Id, "49", "057", "49057000100", amount: 100);
        await h.AddRecordWithGeoidAsync(dataset.Id, "49", "057", "49057000100", amount: 300);

        var total = await h.Service.GetChoroplethAsync(
            org.Id, dataset.Id, user.Id, ChoroplethMetric.TotalAmount, new RecordFilterRequest(), CancellationToken.None);
        var average = await h.Service.GetChoroplethAsync(
            org.Id, dataset.Id, user.Id, ChoroplethMetric.AverageAmount, new RecordFilterRequest(), CancellationToken.None);

        Assert.Equal(400, total.Value![0].Value);
        Assert.Equal(200, average.Value![0].Value);
    }

    [Fact]
    public async Task Records_per_1000_divides_record_count_by_tract_population()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddImportedTractAsync("49", "057");
        await h.AddDemographicsAsync("49057000100", population: 2000);
        await h.AddRecordWithGeoidAsync(dataset.Id, "49", "057", "49057000100");
        await h.AddRecordWithGeoidAsync(dataset.Id, "49", "057", "49057000100");

        var result = await h.Service.GetChoroplethAsync(
            org.Id, dataset.Id, user.Id, ChoroplethMetric.RecordsPer1000, new RecordFilterRequest(), CancellationToken.None);

        // 2 records / (2000 population / 1000) = 1.0 per 1,000 residents.
        Assert.Equal(1.0m, result.Value![0].Value);
    }

    [Fact]
    public async Task A_tract_with_no_matching_records_reports_zero_not_an_error()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddGeocodedRecordAsync(dataset.Id, "49", "057"); // no CensusGeoid set — never touches this tract's records
        await h.AddImportedTractAsync("49", "057");

        var result = await h.Service.GetChoroplethAsync(
            org.Id, dataset.Id, user.Id, ChoroplethMetric.RecordCount, new RecordFilterRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Value!);
        Assert.Equal(0, result.Value![0].Value);
    }

    [Fact]
    public async Task Boundaries_carry_the_real_ffiec_income_level_when_present()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddGeocodedRecordAsync(dataset.Id, "49", "057");
        await h.AddImportedTractAsync("49", "057");
        await h.AddIncomeLevelAsync("49057000100", TractIncomeTier.Moderate, tractMfi: 45_000m, areaMfi: 68_000m);

        var result = await h.Service.GetBoundariesAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        var boundary = Assert.Single(result.Value!);
        Assert.NotNull(boundary.IncomeLevel);
        Assert.Equal(TractIncomeTier.Moderate, boundary.IncomeLevel!.Tier);
        Assert.Equal(45_000m, boundary.IncomeLevel.TractMedianFamilyIncome);
    }

    [Fact]
    public async Task A_tract_with_no_ffiec_row_has_a_null_income_level_not_an_error()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddGeocodedRecordAsync(dataset.Id, "49", "057");
        await h.AddImportedTractAsync("49", "057");

        var result = await h.Service.GetBoundariesAsync(org.Id, dataset.Id, user.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(Assert.Single(result.Value!).IncomeLevel);
    }

    [Fact]
    public async Task IncomeLevel_choropleth_maps_tiers_to_an_ordinal_value_and_hides_unknown()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        await h.AddGeocodedRecordAsync(dataset.Id, "49", "057");
        await h.AddImportedTractAsync("49", "057", tractCode: "000100");
        await h.AddImportedTractAsync("49", "057", tractCode: "000200");
        await h.AddIncomeLevelAsync("49057000100", TractIncomeTier.Low);
        await h.AddIncomeLevelAsync("49057000200", TractIncomeTier.Unknown);

        var result = await h.Service.GetChoroplethAsync(
            org.Id, dataset.Id, user.Id, ChoroplethMetric.IncomeLevel, new RecordFilterRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal((decimal)TractIncomeTier.Low, result.Value!.Single(v => v.Geoid == "49057000100").Value);
        Assert.Null(result.Value!.Single(v => v.Geoid == "49057000200").Value);
    }
}
