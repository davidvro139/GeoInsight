using GeoInsight.Application.Analytics;
using GeoInsight.Application.Common;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Analytics;
using GeoInsight.Infrastructure.Datasets;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Organizations;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GeoInsight.Tests;

public class AnalyticsServiceTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    private sealed class Harness
    {
        public required GeoInsightDbContext DbContext { get; init; }
        public required AnalyticsService Service { get; init; }

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
                Service = new AnalyticsService(dbContext, datasetAccessControl),
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

        public async Task AddTractAsync(string geoid, string countyFips, int population, decimal medianHouseholdIncome)
        {
            var square = Factory.CreatePolygon([
                new Coordinate(0, 0), new Coordinate(1, 0), new Coordinate(1, 1), new Coordinate(0, 1), new Coordinate(0, 0),
            ]);
            DbContext.CensusTracts.Add(new CensusTract
            {
                Geoid = geoid, StateFips = "49", CountyFips = countyFips, TractCode = geoid[5..],
                Boundary = Factory.CreateMultiPolygon([square]),
            });
            DbContext.CensusDemographics.Add(new CensusDemographic
            {
                CensusGeoid = geoid, SurveyYear = 2022, Population = population, MedianHouseholdIncome = medianHouseholdIncome,
            });
            await DbContext.SaveChangesAsync();
        }

        public async Task AddIncomeLevelAsync(string geoid, TractIncomeTier tier)
        {
            DbContext.CensusTractIncomeLevels.Add(new CensusTractIncomeLevel
            {
                CensusGeoid = geoid, SurveyPeriod = "2024-2026", IncomeTier = tier,
            });
            await DbContext.SaveChangesAsync();
        }

        public async Task AddAssessmentAreaCountyAsync(long organizationId, string stateFips, string countyFips)
        {
            DbContext.AssessmentAreaCounties.Add(new AssessmentAreaCounty
            {
                OrganizationId = organizationId, StateFips = stateFips, CountyFips = countyFips,
            });
            await DbContext.SaveChangesAsync();
        }

        public async Task AddRecordAsync(
            long datasetId, string geocodeStatus, decimal? amount = null, string? category = null,
            string? countyFips = null, string? geoid = null, DateOnly? recordDate = null)
        {
            DbContext.DatasetRecords.Add(new DatasetRecord
            {
                DatasetId = datasetId, AddressLine1 = "100 Main St", City = "Ogden", State = "UT",
                GeocodeStatus = geocodeStatus, Amount = amount, Category = category,
                CensusStateFips = countyFips is null ? null : "49", CensusCountyFips = countyFips,
                CensusGeoid = geoid, RecordDate = recordDate,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await DbContext.SaveChangesAsync();
        }
    }

    private static async Task<(Harness H, ApplicationUser User, Organization Org, Dataset Dataset)> SeedStandardDatasetAsync()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);

        await h.AddTractAsync("49057000100", "057", population: 4000, medianHouseholdIncome: 50_000m);
        await h.AddTractAsync("49057000200", "057", population: 2000, medianHouseholdIncome: 90_000m);

        await h.AddRecordAsync(dataset.Id, "matched", amount: 250_000, category: "Mortgage",
            countyFips: "057", geoid: "49057000100", recordDate: new DateOnly(2026, 1, 15));
        await h.AddRecordAsync(dataset.Id, "matched", amount: 18_000, category: "Auto",
            countyFips: "057", geoid: "49057000100", recordDate: new DateOnly(2026, 1, 20));
        await h.AddRecordAsync(dataset.Id, "failed", amount: 410_000, category: "Mortgage",
            countyFips: "057", geoid: "49057000200", recordDate: new DateOnly(2026, 2, 5));
        await h.AddRecordAsync(dataset.Id, "pending"); // no category/county/geoid/date/amount

        return (h, user, org, dataset);
    }

    [Fact]
    public async Task Summary_counts_match_the_seeded_records()
    {
        var (h, user, org, dataset) = await SeedStandardDatasetAsync();

        var result = await h.Service.GetAnalyticsAsync(org.Id, dataset.Id, user.Id, new RecordFilterRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var summary = result.Value!.Summary;
        Assert.Equal(4, summary.RecordCount);
        Assert.Equal(2, summary.GeocodedCount);
        Assert.Equal(50.0m, summary.GeocodedRate);
        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(678_000m, summary.TotalAmount);
        Assert.Equal(2, summary.TractCount);
        Assert.Equal(1, summary.CountyCount);
        Assert.True(summary.DemographicsAreEstimated); // AddTractAsync never sets RawData
    }

    [Fact]
    public async Task DemographicsAreEstimated_is_false_once_every_touched_tract_has_real_data()
    {
        var (h, user, org, dataset) = await SeedStandardDatasetAsync();
        foreach (var row in h.DbContext.CensusDemographics)
        {
            row.RawData = "{\"real\":true}";
        }
        await h.DbContext.SaveChangesAsync();

        var result = await h.Service.GetAnalyticsAsync(org.Id, dataset.Id, user.Id, new RecordFilterRequest(), CancellationToken.None);

        Assert.False(result.Value!.Summary.DemographicsAreEstimated);
    }

    [Fact]
    public async Task Category_and_geocode_status_breakdowns_group_correctly()
    {
        var (h, user, org, dataset) = await SeedStandardDatasetAsync();

        var result = await h.Service.GetAnalyticsAsync(org.Id, dataset.Id, user.Id, new RecordFilterRequest(), CancellationToken.None);

        var byCategory = result.Value!.ByCategory.ToDictionary(c => c.Category ?? "(null)", c => c.Count);
        Assert.Equal(2, byCategory["Mortgage"]);
        Assert.Equal(1, byCategory["Auto"]);
        Assert.Equal(1, byCategory["(null)"]);

        var byStatus = result.Value.ByGeocodeStatus.ToDictionary(s => s.Status, s => s.Count);
        Assert.Equal(2, byStatus["matched"]);
        Assert.Equal(1, byStatus["failed"]);
        Assert.Equal(1, byStatus["pending"]);
    }

    [Fact]
    public async Task Income_range_buckets_records_by_their_tracts_income_with_an_unknown_bucket()
    {
        var (h, user, org, dataset) = await SeedStandardDatasetAsync();

        var result = await h.Service.GetAnalyticsAsync(org.Id, dataset.Id, user.Id, new RecordFilterRequest(), CancellationToken.None);

        var byRange = result.Value!.ByIncomeRange.ToDictionary(r => r.Range, r => r.Count);
        Assert.Equal(2, byRange["$40k–$60k"]); // the two records in the $50k tract
        Assert.Equal(1, byRange["$80k–$100k"]); // the one record in the $90k tract
        Assert.Equal(1, byRange["Unknown"]); // the record with no geoid at all
    }

    [Fact]
    public async Task Time_series_buckets_by_month_and_excludes_records_with_no_date()
    {
        var (h, user, org, dataset) = await SeedStandardDatasetAsync();

        var result = await h.Service.GetAnalyticsAsync(org.Id, dataset.Id, user.Id, new RecordFilterRequest(), CancellationToken.None);

        var byMonth = result.Value!.OverTime.ToDictionary(p => p.Period, p => p.Count);
        Assert.Equal(2, byMonth["2026-01"]);
        Assert.Equal(1, byMonth["2026-02"]);
        Assert.Equal(2, result.Value.OverTime.Count); // the undated record contributes to no bucket
    }

    [Fact]
    public async Task Tract_statistics_report_population_count_per1000_and_amount_per_tract()
    {
        var (h, user, org, dataset) = await SeedStandardDatasetAsync();

        var result = await h.Service.GetAnalyticsAsync(org.Id, dataset.Id, user.Id, new RecordFilterRequest(), CancellationToken.None);

        var stats = result.Value!.TractStatistics.ToDictionary(t => t.Geoid);
        var tractA = stats["49057000100"];
        Assert.Equal(4000, tractA.Population);
        Assert.Equal(2, tractA.RecordCount);
        Assert.Equal(268_000m, tractA.TotalAmount);
        Assert.Equal(0.5m, tractA.RecordsPer1000); // 2 records / (4000 population / 1000)

        var tractB = stats["49057000200"];
        Assert.Equal(1, tractB.RecordCount);
        Assert.Equal(410_000m, tractB.TotalAmount);
    }

    [Fact]
    public async Task Tract_statistics_surface_the_real_ffiec_income_tier_when_present()
    {
        var (h, user, org, dataset) = await SeedStandardDatasetAsync();
        await h.AddIncomeLevelAsync("49057000100", TractIncomeTier.Low);

        var result = await h.Service.GetAnalyticsAsync(org.Id, dataset.Id, user.Id, new RecordFilterRequest(), CancellationToken.None);

        var stats = result.Value!.TractStatistics.ToDictionary(t => t.Geoid);
        Assert.Equal(TractIncomeTier.Low, stats["49057000100"].IncomeTier);
        Assert.Null(stats["49057000200"].IncomeTier); // no FFIEC row for this tract
    }

    [Fact]
    public async Task With_no_assessment_area_configured_every_tract_reports_null_not_false()
    {
        var (h, user, org, dataset) = await SeedStandardDatasetAsync();

        var result = await h.Service.GetAnalyticsAsync(org.Id, dataset.Id, user.Id, new RecordFilterRequest(), CancellationToken.None);

        Assert.False(result.Value!.Summary.HasAssessmentArea);
        Assert.All(result.Value.TractStatistics, t => Assert.Null(t.InAssessmentArea));
    }

    [Fact]
    public async Task Tract_statistics_flag_whether_a_tract_falls_inside_the_declared_assessment_area()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);

        await h.AddTractAsync("49001000100", "001", population: 4000, medianHouseholdIncome: 50_000m);
        await h.AddTractAsync("49002000100", "002", population: 1000, medianHouseholdIncome: 50_000m);
        await h.AddRecordAsync(dataset.Id, "matched", amount: 100_000, countyFips: "001", geoid: "49001000100");
        await h.AddRecordAsync(dataset.Id, "matched", amount: 100_000, countyFips: "002", geoid: "49002000100");
        await h.AddAssessmentAreaCountyAsync(org.Id, "49", "001"); // only county 001 is declared

        var result = await h.Service.GetAnalyticsAsync(org.Id, dataset.Id, user.Id, new RecordFilterRequest(), CancellationToken.None);

        Assert.True(result.Value!.Summary.HasAssessmentArea);
        var stats = result.Value.TractStatistics.ToDictionary(t => t.Geoid);
        Assert.True(stats["49001000100"].InAssessmentArea);
        Assert.False(stats["49002000100"].InAssessmentArea);
    }

    [Fact]
    public async Task Assessment_area_membership_is_scoped_to_the_owning_organization()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        var (otherOrg, _) = await h.AddOrgAndDatasetAsync(await h.AddUserAsync("other@example.com"), OrganizationRole.Viewer);

        await h.AddTractAsync("49057000100", "057", population: 4000, medianHouseholdIncome: 50_000m);
        await h.AddRecordAsync(dataset.Id, "matched", amount: 100_000, countyFips: "057", geoid: "49057000100");
        // Declared for a *different* organization — must not leak into this one's analytics.
        await h.AddAssessmentAreaCountyAsync(otherOrg.Id, "49", "057");

        var result = await h.Service.GetAnalyticsAsync(org.Id, dataset.Id, user.Id, new RecordFilterRequest(), CancellationToken.None);

        Assert.False(result.Value!.Summary.HasAssessmentArea);
        Assert.Null(result.Value.TractStatistics.Single().InAssessmentArea);
    }

    [Fact]
    public async Task Tract_statistics_excludes_tracts_with_no_records_even_though_they_are_in_scope()
    {
        var (h, user, org, dataset) = await SeedStandardDatasetAsync();
        // A third tract in the same already-imported county, but with no dataset records —
        // in scope for the Map tab's boundary layer, but not "geographic analysis".
        await h.AddTractAsync("49057000300", "057", population: 3000, medianHouseholdIncome: 70_000m);

        var result = await h.Service.GetAnalyticsAsync(org.Id, dataset.Id, user.Id, new RecordFilterRequest(), CancellationToken.None);

        Assert.Equal(2, result.Value!.TractStatistics.Count);
        Assert.DoesNotContain(result.Value.TractStatistics, t => t.Geoid == "49057000300");
    }

    [Fact]
    public async Task Tract_statistics_compare_records_per_1000_against_county_and_region_averages()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);

        // County 001: tract A1 (1.0 records/1000) and A2 (0.5 records/1000) -> county average 0.75.
        await h.AddTractAsync("49001000100", "001", population: 4000, medianHouseholdIncome: 50_000m);
        await h.AddTractAsync("49001000200", "001", population: 2000, medianHouseholdIncome: 50_000m);
        // County 002: a single tract B1 (3.0 records/1000) -> county average is just its own value.
        await h.AddTractAsync("49002000100", "002", population: 1000, medianHouseholdIncome: 50_000m);

        for (var i = 0; i < 4; i++)
        {
            await h.AddRecordAsync(dataset.Id, "matched", amount: 100_000, countyFips: "001", geoid: "49001000100");
        }
        await h.AddRecordAsync(dataset.Id, "matched", amount: 50_000, countyFips: "001", geoid: "49001000200");
        for (var i = 0; i < 3; i++)
        {
            await h.AddRecordAsync(dataset.Id, "matched", amount: 100_000, countyFips: "002", geoid: "49002000100");
        }

        var result = await h.Service.GetAnalyticsAsync(org.Id, dataset.Id, user.Id, new RecordFilterRequest(), CancellationToken.None);

        var stats = result.Value!.TractStatistics.ToDictionary(t => t.Geoid);
        var a1 = stats["49001000100"];
        var a2 = stats["49001000200"];
        var b1 = stats["49002000100"];

        Assert.Equal(1.0m, a1.RecordsPer1000);
        Assert.Equal(0.5m, a2.RecordsPer1000);
        Assert.Equal(3.0m, b1.RecordsPer1000);

        Assert.Equal(100_000m, a1.AverageAmount);
        Assert.Equal(50_000m, a2.AverageAmount);

        // Same county -> same county average, computed from both tracts' values, not just one.
        Assert.Equal(0.75m, a1.CountyAverageRecordsPer1000);
        Assert.Equal(0.75m, a2.CountyAverageRecordsPer1000);
        Assert.Equal(3.0m, b1.CountyAverageRecordsPer1000); // a county of one tract averages to itself

        // Region average is identical on every row regardless of county: (1.0 + 0.5 + 3.0) / 3.
        Assert.Equal(1.5m, a1.RegionAverageRecordsPer1000);
        Assert.Equal(1.5m, a2.RegionAverageRecordsPer1000);
        Assert.Equal(1.5m, b1.RegionAverageRecordsPer1000);

        // Deviation is against the *county* average: (1.0 - 0.75) / 0.75 * 100 = +33.3%.
        Assert.Equal(33.3m, a1.DeviationFromCountyAveragePercent);
        Assert.Equal(-33.3m, a2.DeviationFromCountyAveragePercent);
        Assert.Equal(0m, b1.DeviationFromCountyAveragePercent); // a lone tract never deviates from its own county average
    }

    [Fact]
    public async Task Applying_a_filter_recalculates_the_summary_and_breakdowns_together()
    {
        var (h, user, org, dataset) = await SeedStandardDatasetAsync();

        var filter = new RecordFilterRequest(Category: "Mortgage");
        var result = await h.Service.GetAnalyticsAsync(org.Id, dataset.Id, user.Id, filter, CancellationToken.None);

        var summary = result.Value!.Summary;
        Assert.Equal(2, summary.RecordCount); // only the two Mortgage records
        Assert.Equal(1, summary.GeocodedCount); // the failed Mortgage record doesn't count
        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(660_000m, summary.TotalAmount); // 250k + 410k, the Auto record's 18k excluded
        Assert.Single(result.Value.ByCategory);
        Assert.Equal("Mortgage", result.Value.ByCategory[0].Category);
        Assert.Equal(2, result.Value.ByCategory[0].Count);
    }

    [Fact]
    public async Task A_dataset_from_another_organization_is_not_found()
    {
        var h = Harness.Create();
        var userA = await h.AddUserAsync("a@example.com");
        var userB = await h.AddUserAsync("b@example.com");
        var (_, datasetA) = await h.AddOrgAndDatasetAsync(userA, OrganizationRole.Administrator);
        var (orgB, _) = await h.AddOrgAndDatasetAsync(userB, OrganizationRole.Administrator);

        var result = await h.Service.GetAnalyticsAsync(orgB.Id, datasetA.Id, userB.Id, new RecordFilterRequest(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task A_viewer_can_read_analytics()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);

        var result = await h.Service.GetAnalyticsAsync(org.Id, dataset.Id, user.Id, new RecordFilterRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.Summary.RecordCount);
    }
}
