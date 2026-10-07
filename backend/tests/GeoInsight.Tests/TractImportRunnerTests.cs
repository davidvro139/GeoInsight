using GeoInsight.Application.Tracts;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using GeoInsight.Infrastructure.Tracts;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;

namespace GeoInsight.Tests;

/// <summary>
/// Uses a fake IStateTractSource (two hand-built square polygons at known
/// coordinates, no live network/shapefile parsing) to exercise the runner's
/// own logic: Polygon-to-MultiPolygon normalization, GEOID-skip idempotency,
/// and point-in-polygon backfill for manually-entered-coordinate records.
/// </summary>
public class TractImportRunnerTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    // Two adjacent, non-overlapping 1x1-degree squares in fake "county 001".
    private static Polygon Square(double minX, double minY, double maxX, double maxY) =>
        Factory.CreatePolygon([
            new Coordinate(minX, minY), new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY), new Coordinate(minX, maxY),
            new Coordinate(minX, minY),
        ]);

    private static readonly ParsedTractFeature TractOne =
        new("99001000100", "001", "000100", "Census Tract 1", Square(-112, 41, -111, 42));

    private static readonly ParsedTractFeature TractTwo =
        new("99001000200", "001", "000200", "Census Tract 2", Square(-111, 41, -110, 42));

    private sealed class FakeStateTractSource(IReadOnlyList<ParsedTractFeature> features) : IStateTractSource
    {
        public Task<IReadOnlyList<ParsedTractFeature>> GetTractsAsync(string stateFips, CancellationToken cancellationToken) =>
            Task.FromResult(features);
    }

    // Implements IAcsDemographicDataSource (not just IDemographicDataSource) so
    // the same fake can stand in for either the placeholder slot or the real
    // ACS slot in CreateRunner — the two interfaces share one member.
    private sealed class FakeDemographicDataSource(Func<string, ParsedDemographics> respond) : IAcsDemographicDataSource
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ParsedDemographics>> GetDemographicsAsync(
            IReadOnlyList<string> geoids, string? apiKey, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<ParsedDemographics>>(geoids.Select(respond).ToList());
        }
    }

    // Real FfiecTractIncomeSource reads a bundled CSV that isn't copied next to
    // the test assembly's output — these tests care about tract-import
    // behavior, not the FFIEC lookup itself (that's FfiecTractIncomeSourceTests),
    // so this fake returns nothing by default and a couple of tests below hand
    // it a known record to verify the upsert wiring.
    private sealed class FakeFfiecTractIncomeSource(IReadOnlyDictionary<string, FfiecTractIncomeRecord>? records = null) : IFfiecTractIncomeSource
    {
        public bool TryGetValue(string geoid, out FfiecTractIncomeRecord record) =>
            (records ?? new Dictionary<string, FfiecTractIncomeRecord>()).TryGetValue(geoid, out record!);
    }

    private static GeoInsightDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<GeoInsightDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static readonly IDataProtectionProvider DataProtection =
        new ServiceCollection().AddDataProtection().Services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();

    private static async Task SeedDatasetAsync(GeoInsightDbContext dbContext, long id = 1, long organizationId = 1)
    {
        dbContext.Datasets.Add(new Dataset
        {
            Id = id, OrganizationId = organizationId, Name = "Test Dataset", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();
    }

    // The real placeholder generator has no I/O — fine to use directly in tests
    // that don't care about demographics, rather than faking something with no
    // external dependency to fake. No organization needs seeding for the
    // no-API-key path: TractImportRunner tolerates a missing Organization row
    // (SingleOrDefaultAsync) and just treats it as "no key configured".
    private static TractImportRunner CreateRunner(
        GeoInsightDbContext dbContext, IStateTractSource source, IDemographicDataSource? placeholder = null, IAcsDemographicDataSource? acs = null,
        IFfiecTractIncomeSource? ffiecTractIncomeSource = null) =>
        new(dbContext, source, placeholder ?? new PlaceholderDemographicDataSource(),
            acs ?? new FakeDemographicDataSource(_ => throw new InvalidOperationException("ACS source should not be called without an organization API key")),
            ffiecTractIncomeSource ?? new FakeFfiecTractIncomeSource(),
            DataProtection, NullLogger<TractImportRunner>.Instance);

    [Fact]
    public async Task Importing_inserts_tracts_as_multipolygon_with_srid_4326()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne, TractTwo]));

        await runner.RunAsync(datasetId: 1, stateFips: "99", countyFipsList: ["001"], new JobCancellationToken(false));

        var tracts = await dbContext.CensusTracts.OrderBy(t => t.Geoid).ToListAsync();
        Assert.Equal(2, tracts.Count);
        Assert.All(tracts, t => Assert.IsType<MultiPolygon>(t.Boundary));
        Assert.All(tracts, t => Assert.Equal(4326, t.Boundary.SRID));
        Assert.Equal("99001000100", tracts[0].Geoid);
    }

    [Fact]
    public async Task Reimporting_an_already_present_geoid_does_not_duplicate_it()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne]));

        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));
        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));

        Assert.Equal(1, await dbContext.CensusTracts.CountAsync());
    }

    [Fact]
    public async Task A_manually_coordinated_record_inside_a_tract_gets_backfilled()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        dbContext.DatasetRecords.Add(new DatasetRecord
        {
            DatasetId = 1, AddressLine1 = "Manual entry", City = "Nowhere", State = "ZZ",
            GeocodeStatus = "manually_resolved", GeocoderSource = "manual",
            Latitude = 41.5m, Longitude = -111.5m, // inside TractOne's square
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne, TractTwo]));
        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));

        var record = await dbContext.DatasetRecords.SingleAsync();
        Assert.Equal("99001000100", record.CensusGeoid);
        Assert.Equal("000100", record.CensusTract);
        Assert.Equal("001", record.CensusCountyFips);
        Assert.Equal("99", record.CensusStateFips);
    }

    [Fact]
    public async Task A_record_outside_every_tract_is_left_unassigned()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        dbContext.DatasetRecords.Add(new DatasetRecord
        {
            DatasetId = 1, AddressLine1 = "Far away", City = "Nowhere", State = "ZZ",
            GeocodeStatus = "manually_resolved", GeocoderSource = "manual",
            Latitude = 10m, Longitude = 10m, // nowhere near either fake square
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne, TractTwo]));
        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));

        var record = await dbContext.DatasetRecords.SingleAsync();
        Assert.Null(record.CensusGeoid);
    }

    [Fact]
    public async Task An_already_geocoded_record_is_left_untouched()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        dbContext.DatasetRecords.Add(new DatasetRecord
        {
            DatasetId = 1, AddressLine1 = "Already geocoded", City = "Nowhere", State = "ZZ",
            GeocodeStatus = "matched", GeocoderSource = "census",
            Latitude = 41.5m, Longitude = -111.5m, CensusGeoid = "existing-geoid",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne, TractTwo]));
        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));

        var record = await dbContext.DatasetRecords.SingleAsync();
        Assert.Equal("existing-geoid", record.CensusGeoid); // untouched
    }

    [Fact]
    public async Task Backfill_is_scoped_to_the_triggering_dataset_only()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        dbContext.DatasetRecords.Add(new DatasetRecord
        {
            DatasetId = 2, AddressLine1 = "Different dataset", City = "Nowhere", State = "ZZ",
            GeocodeStatus = "manually_resolved", GeocoderSource = "manual",
            Latitude = 41.5m, Longitude = -111.5m,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne, TractTwo]));
        await runner.RunAsync(datasetId: 1, "99", ["001"], new JobCancellationToken(false)); // triggered by dataset 1

        var record = await dbContext.DatasetRecords.SingleAsync();
        Assert.Null(record.CensusGeoid); // dataset 2's record untouched by dataset 1's import
    }

    [Fact]
    public async Task BackfillOnly_assigns_a_csv_coordinate_record_with_no_known_county()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        // Tracts already imported via an earlier run (or another dataset entirely) —
        // BackfillOnlyAsync never calls the tract source, it only reads what's
        // already in CensusTracts.
        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne, TractTwo]));
        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));

        dbContext.DatasetRecords.Add(new DatasetRecord
        {
            DatasetId = 5, AddressLine1 = "123 CSV Ave", City = "Nowhere", State = "ZZ",
            GeocodeStatus = "matched", GeocoderSource = "csv_import",
            Latitude = 41.5m, Longitude = -111.5m, // inside TractOne's square
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        await runner.BackfillOnlyAsync(5, new JobCancellationToken(false));

        var record = await dbContext.DatasetRecords.SingleAsync(r => r.DatasetId == 5);
        Assert.Equal("99001000100", record.CensusGeoid);
        Assert.Equal("99", record.CensusStateFips);
        Assert.Equal("001", record.CensusCountyFips);
    }

    [Fact]
    public async Task Newly_imported_tracts_get_paired_demographic_rows()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        var demographics = new FakeDemographicDataSource(geoid => new ParsedDemographics(
            geoid, Population: 4000, MedianHouseholdIncome: 65_000m, PovertyRate: 12m, MedianAge: 38m,
            OwnerOccupiedPercent: 60m, RenterOccupiedPercent: 40m, MinorityPopulationPercent: 25m));
        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne, TractTwo]), demographics);

        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));

        var rows = await dbContext.CensusDemographics.OrderBy(d => d.CensusGeoid).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("99001000100", rows[0].CensusGeoid);
        Assert.Equal(2022, rows[0].SurveyYear);
        Assert.Equal(4000, rows[0].Population);
        Assert.Equal(65_000m, rows[0].MedianHouseholdIncome);
        Assert.Equal(1, demographics.CallCount);
    }

    [Fact]
    public async Task Reimporting_does_not_duplicate_or_regenerate_demographic_rows()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        var demographics = new FakeDemographicDataSource(geoid => new ParsedDemographics(
            geoid, 4000, 65_000m, 12m, 38m, 60m, 40m, 25m));
        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne]), demographics);

        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));
        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));

        Assert.Equal(1, await dbContext.CensusDemographics.CountAsync());
        Assert.Equal(1, demographics.CallCount); // second run found no new tracts, never called the source again
    }

    [Fact]
    public async Task An_organization_with_a_configured_key_uses_the_real_acs_source_not_the_placeholder()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext, organizationId: 7);
        dbContext.Organizations.Add(new Organization
        {
            Id = 7, Name = "Has A Key", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            CensusApiKeyEncrypted = DataProtection.CreateProtector("CensusApiKey").Protect("real-key"),
        });
        await dbContext.SaveChangesAsync();

        var placeholder = new FakeDemographicDataSource(_ => throw new InvalidOperationException("placeholder should not be called when a key is configured"));
        var acs = new FakeDemographicDataSource(geoid => new ParsedDemographics(
            geoid, 9000, 80_000m, 8m, 40m, 65m, 35m, 30m, RawJson: "{\"real\":true}"));
        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne, TractTwo]), placeholder, acs);

        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));

        Assert.Equal(1, acs.CallCount);
        var row = await dbContext.CensusDemographics.SingleAsync(d => d.CensusGeoid == "99001000100");
        Assert.Equal(9000, row.Population);
        Assert.Equal("{\"real\":true}", row.RawData);
    }

    [Fact]
    public async Task A_failing_real_source_falls_back_to_placeholder_instead_of_failing_the_job()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext, organizationId: 8);
        dbContext.Organizations.Add(new Organization
        {
            Id = 8, Name = "Bad Key", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            CensusApiKeyEncrypted = DataProtection.CreateProtector("CensusApiKey").Protect("bad-key"),
        });
        await dbContext.SaveChangesAsync();

        var placeholder = new FakeDemographicDataSource(geoid => new ParsedDemographics(
            geoid, 4000, 65_000m, 12m, 38m, 60m, 40m, 25m));
        var acs = new FakeDemographicDataSource(_ => throw new InvalidOperationException("simulated ACS outage"));
        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne, TractTwo]), placeholder, acs);

        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));

        var row = await dbContext.CensusDemographics.SingleAsync(d => d.CensusGeoid == "99001000100");
        Assert.Equal(4000, row.Population);
        Assert.Null(row.RawData); // fell back to placeholder, which never sets RawData
    }

    [Fact]
    public async Task RefreshDemographicsAsync_upgrades_an_existing_synthetic_row_in_place_without_duplicating()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext, organizationId: 9);
        dbContext.Organizations.Add(new Organization
        {
            Id = 9, Name = "Upgrades", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            CensusApiKeyEncrypted = DataProtection.CreateProtector("CensusApiKey").Protect("real-key"),
        });
        await dbContext.SaveChangesAsync();

        // First import with no key — gets synthetic data.
        var placeholder = new FakeDemographicDataSource(geoid => new ParsedDemographics(
            geoid, 4000, 65_000m, 12m, 38m, 60m, 40m, 25m));
        var runnerNoKey = CreateRunner(dbContext, new FakeStateTractSource([TractOne]), placeholder);
        await runnerNoKey.RunAsync(1, "99", ["001"], new JobCancellationToken(false));
        Assert.Equal(1, await dbContext.CensusDemographics.CountAsync());

        // Now the org gets a real key and asks for a refresh.
        var acs = new FakeDemographicDataSource(geoid => new ParsedDemographics(
            geoid, 9500, 91_000m, 6m, 41m, 70m, 30m, 18m, RawJson: "{\"real\":true}"));
        var runnerWithKey = CreateRunner(dbContext, new FakeStateTractSource([TractOne]), placeholder, acs);
        await runnerWithKey.RefreshDemographicsAsync(1, ["99001000100"], new JobCancellationToken(false));

        Assert.Equal(1, await dbContext.CensusDemographics.CountAsync()); // upgraded in place, not duplicated
        var row = await dbContext.CensusDemographics.SingleAsync();
        Assert.Equal(9500, row.Population);
        Assert.Equal("{\"real\":true}", row.RawData);
    }

    [Fact]
    public async Task ImportStandaloneCountyAsync_imports_tracts_with_no_dataset_involved_at_all()
    {
        await using var dbContext = CreateContext();
        // Deliberately no Dataset/Organization row seeded — the whole point of
        // this entry point is that it needs neither.
        var placeholder = new FakeDemographicDataSource(geoid => new ParsedDemographics(
            geoid, 4000, 65_000m, 12m, 38m, 60m, 40m, 25m));
        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne, TractTwo]), placeholder);

        await runner.ImportStandaloneCountyAsync(organizationId: 1, "99", "001", new JobCancellationToken(false));

        var tracts = await dbContext.CensusTracts.OrderBy(t => t.Geoid).ToListAsync();
        Assert.Equal(2, tracts.Count);
        var demographics = await dbContext.CensusDemographics.CountAsync();
        Assert.Equal(2, demographics);
    }

    [Fact]
    public async Task ImportStandaloneCountyAsync_force_refreshes_demographics_even_for_an_already_imported_tract()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        var placeholder = new FakeDemographicDataSource(geoid => new ParsedDemographics(
            geoid, 4000, 65_000m, 12m, 38m, 60m, 40m, 25m));
        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne]), placeholder);

        // First, the normal dataset-triggered path — creates a synthetic row.
        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));
        Assert.Null((await dbContext.CensusDemographics.SingleAsync()).RawData);

        // Now the org gets a key and pre-loads the same county via the standalone path.
        var acs = new FakeDemographicDataSource(geoid => new ParsedDemographics(
            geoid, 9999, 99_000m, 5m, 42m, 71m, 29m, 15m, RawJson: "{\"real\":true}"));
        dbContext.Organizations.Add(new Organization
        {
            Id = 1, Name = "Standalone Import Org", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            CensusApiKeyEncrypted = DataProtection.CreateProtector("CensusApiKey").Protect("real-key"),
        });
        await dbContext.SaveChangesAsync();
        var runnerWithKey = CreateRunner(dbContext, new FakeStateTractSource([TractOne]), placeholder, acs);

        await runnerWithKey.ImportStandaloneCountyAsync(organizationId: 1, "99", "001", new JobCancellationToken(false));

        Assert.Equal(1, await dbContext.CensusTracts.CountAsync()); // no duplicate tract
        var row = await dbContext.CensusDemographics.SingleAsync(); // upgraded in place, not duplicated
        Assert.Equal(9999, row.Population);
        Assert.Equal("{\"real\":true}", row.RawData);
    }

    [Fact]
    public async Task Newly_imported_tracts_get_paired_income_level_rows_from_the_ffiec_source()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        var ffiec = new FakeFfiecTractIncomeSource(new Dictionary<string, FfiecTractIncomeRecord>
        {
            ["99001000100"] = new("99001000100", TractIncomeTier.Moderate, 45_000m, 68_000m, 66.18m, "TEST, ZZ"),
        });
        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne, TractTwo]), ffiecTractIncomeSource: ffiec);

        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));

        var rows = await dbContext.CensusTractIncomeLevels.ToListAsync();
        Assert.Single(rows); // TractTwo has no matching FFIEC record, so it gets no row
        var row = rows[0];
        Assert.Equal("99001000100", row.CensusGeoid);
        Assert.Equal(TractIncomeTier.Moderate, row.IncomeTier);
        Assert.Equal(45_000m, row.TractMedianFamilyIncome);
        Assert.Equal(68_000m, row.AreaMedianFamilyIncome);
        Assert.Equal(66.18m, row.IncomePercentageOfAreaMedian);
        Assert.Equal("TEST, ZZ", row.MsaMdName);
    }

    [Fact]
    public async Task Reimporting_does_not_duplicate_income_level_rows()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        var ffiec = new FakeFfiecTractIncomeSource(new Dictionary<string, FfiecTractIncomeRecord>
        {
            ["99001000100"] = new("99001000100", TractIncomeTier.Low, 30_000m, 68_000m, 44.12m, "TEST, ZZ"),
        });
        var runner = CreateRunner(dbContext, new FakeStateTractSource([TractOne]), ffiecTractIncomeSource: ffiec);

        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));
        await runner.RunAsync(1, "99", ["001"], new JobCancellationToken(false));

        Assert.Equal(1, await dbContext.CensusTractIncomeLevels.CountAsync());
    }
}
