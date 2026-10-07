using GeoInsight.Application.Common;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using GeoInsight.Infrastructure.Records;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Tests;

/// <summary>
/// Exercises ApplyFilters directly against a raw DbContext (no access
/// control) — one field at a time and combined — since this is the single
/// place every read path (table, map, choropleth) shares filtering logic.
/// </summary>
public class RecordFilterQueryExtensionsTests
{
    private static GeoInsightDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<GeoInsightDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DatasetRecord Record(
        long datasetId, string addressLine1, string city = "Ogden", string state = "UT",
        decimal? amount = null, string? category = null, DateOnly? recordDate = null,
        string? countyFips = null, string? tract = null, string? geocodeStatus = "pending", string? geoid = null) => new()
    {
        DatasetId = datasetId, AddressLine1 = addressLine1, City = city, State = state,
        Amount = amount, Category = category, RecordDate = recordDate,
        CensusCountyFips = countyFips, CensusTract = tract, GeocodeStatus = geocodeStatus!, CensusGeoid = geoid,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task Date_range_includes_only_records_within_it()
    {
        await using var dbContext = CreateContext();
        dbContext.DatasetRecords.AddRange(
            Record(1, "Before", recordDate: new DateOnly(2026, 1, 1)),
            Record(1, "Inside", recordDate: new DateOnly(2026, 2, 15)),
            Record(1, "After", recordDate: new DateOnly(2026, 3, 1)),
            Record(1, "NoDate"));
        await dbContext.SaveChangesAsync();

        var filter = new RecordFilterRequest(DateFrom: new DateOnly(2026, 2, 1), DateTo: new DateOnly(2026, 2, 28));
        var result = await dbContext.DatasetRecords.Where(r => r.DatasetId == 1).ApplyFilters(filter, dbContext).ToListAsync();

        Assert.Equal(["Inside"], result.Select(r => r.AddressLine1));
    }

    [Fact]
    public async Task Category_filter_is_a_case_sensitive_substring_match()
    {
        await using var dbContext = CreateContext();
        dbContext.DatasetRecords.AddRange(
            Record(1, "A", category: "Mortgage Loan"),
            Record(1, "B", category: "Auto Loan"),
            Record(1, "C", category: null));
        await dbContext.SaveChangesAsync();

        var filter = new RecordFilterRequest(Category: "Loan");
        var result = await dbContext.DatasetRecords.Where(r => r.DatasetId == 1).ApplyFilters(filter, dbContext).ToListAsync();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Amount_range_is_inclusive_on_both_ends()
    {
        await using var dbContext = CreateContext();
        dbContext.DatasetRecords.AddRange(
            Record(1, "Low", amount: 100), Record(1, "AtMin", amount: 200),
            Record(1, "AtMax", amount: 400), Record(1, "High", amount: 500));
        await dbContext.SaveChangesAsync();

        var filter = new RecordFilterRequest(MinAmount: 200, MaxAmount: 400);
        var result = await dbContext.DatasetRecords.Where(r => r.DatasetId == 1).ApplyFilters(filter, dbContext).ToListAsync();

        Assert.Equal(["AtMax", "AtMin"], result.Select(r => r.AddressLine1).OrderBy(x => x));
    }

    [Fact]
    public async Task County_and_tract_filters_are_exact_matches()
    {
        await using var dbContext = CreateContext();
        dbContext.DatasetRecords.AddRange(
            Record(1, "Match", countyFips: "057", tract: "201100"),
            Record(1, "WrongCounty", countyFips: "035", tract: "201100"),
            Record(1, "WrongTract", countyFips: "057", tract: "999999"));
        await dbContext.SaveChangesAsync();

        var filter = new RecordFilterRequest(CountyFips: "057", Tract: "201100");
        var result = await dbContext.DatasetRecords.Where(r => r.DatasetId == 1).ApplyFilters(filter, dbContext).ToListAsync();

        Assert.Equal(["Match"], result.Select(r => r.AddressLine1));
    }

    [Fact]
    public async Task Geocode_status_filter_is_an_exact_match()
    {
        await using var dbContext = CreateContext();
        dbContext.DatasetRecords.AddRange(
            Record(1, "Matched", geocodeStatus: "matched"),
            Record(1, "Failed", geocodeStatus: "failed"));
        await dbContext.SaveChangesAsync();

        var filter = new RecordFilterRequest(GeocodeStatus: "matched");
        var result = await dbContext.DatasetRecords.Where(r => r.DatasetId == 1).ApplyFilters(filter, dbContext).ToListAsync();

        Assert.Equal(["Matched"], result.Select(r => r.AddressLine1));
    }

    [Fact]
    public async Task Median_income_range_joins_through_the_records_tract_geoid()
    {
        await using var dbContext = CreateContext();
        dbContext.CensusDemographics.AddRange(
            new CensusDemographic { CensusGeoid = "geoid-rich", SurveyYear = 2022, MedianHouseholdIncome = 110_000m },
            new CensusDemographic { CensusGeoid = "geoid-poor", SurveyYear = 2022, MedianHouseholdIncome = 30_000m });
        dbContext.DatasetRecords.AddRange(
            Record(1, "Rich", geoid: "geoid-rich"),
            Record(1, "Poor", geoid: "geoid-poor"),
            Record(1, "NoGeoid", geoid: null));
        await dbContext.SaveChangesAsync();

        var filter = new RecordFilterRequest(MinMedianIncome: 50_000m);
        var result = await dbContext.DatasetRecords.Where(r => r.DatasetId == 1).ApplyFilters(filter, dbContext).ToListAsync();

        Assert.Equal(["Rich"], result.Select(r => r.AddressLine1));
    }

    [Fact]
    public async Task Combined_filters_are_ANDed_together()
    {
        await using var dbContext = CreateContext();
        dbContext.DatasetRecords.AddRange(
            Record(1, "MatchesBoth", category: "Mortgage", amount: 300),
            Record(1, "WrongCategory", category: "Auto", amount: 300),
            Record(1, "WrongAmount", category: "Mortgage", amount: 900));
        await dbContext.SaveChangesAsync();

        var filter = new RecordFilterRequest(Category: "Mortgage", MinAmount: 100, MaxAmount: 500);
        var result = await dbContext.DatasetRecords.Where(r => r.DatasetId == 1).ApplyFilters(filter, dbContext).ToListAsync();

        Assert.Equal(["MatchesBoth"], result.Select(r => r.AddressLine1));
    }

    [Fact]
    public async Task An_empty_filter_excludes_nothing()
    {
        await using var dbContext = CreateContext();
        dbContext.DatasetRecords.AddRange(Record(1, "A"), Record(1, "B"));
        await dbContext.SaveChangesAsync();

        var result = await dbContext.DatasetRecords.Where(r => r.DatasetId == 1)
            .ApplyFilters(new RecordFilterRequest(), dbContext).ToListAsync();

        Assert.Equal(2, result.Count);
    }
}
