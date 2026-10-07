using GeoInsight.Infrastructure.Tracts;

namespace GeoInsight.Tests;

/// <summary>
/// Verifies the synthetic-data generator's own guarantees: deterministic per
/// GEOID (not re-randomized on every call), distinct GEOIDs get distinct
/// values, and every value stays within the documented plausible range.
/// This is NOT testing real Census statistics — see the class's own doc
/// comment for why a real ACS integration isn't built.
/// </summary>
public class PlaceholderDemographicDataSourceTests
{
    private readonly PlaceholderDemographicDataSource _source = new();

    [Fact]
    public async Task The_same_geoid_always_yields_identical_values()
    {
        var first = (await _source.GetDemographicsAsync(["49057201100"], apiKey: null, CancellationToken.None)).Single();
        var second = (await _source.GetDemographicsAsync(["49057201100"], apiKey: null, CancellationToken.None)).Single();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Different_geoids_yield_different_values()
    {
        var results = await _source.GetDemographicsAsync(["49057201100", "49057200600"], apiKey: null, CancellationToken.None);

        Assert.NotEqual(results[0].Population, results[1].Population);
    }

    [Fact]
    public async Task Values_stay_within_documented_plausible_ranges()
    {
        var geoids = Enumerable.Range(0, 50).Select(i => $"fake-geoid-{i}").ToList();
        var results = await _source.GetDemographicsAsync(geoids, apiKey: null, CancellationToken.None);

        Assert.All(results, r =>
        {
            Assert.InRange(r.Population, 1200, 8000);
            Assert.InRange(r.MedianHouseholdIncome, 35_000m, 120_000m);
            Assert.InRange(r.PovertyRate, 3m, 25m);
            Assert.InRange(r.MedianAge, 28m, 55m);
            Assert.InRange(r.OwnerOccupiedPercent, 40m, 80m);
            Assert.InRange(r.RenterOccupiedPercent, 20m, 60m);
            Assert.InRange(r.MinorityPopulationPercent, 5m, 60m);
        });
    }

    [Fact]
    public async Task Owner_and_renter_occupied_percentages_sum_to_100()
    {
        var results = await _source.GetDemographicsAsync(["a", "b", "c"], apiKey: null, CancellationToken.None);

        Assert.All(results, r => Assert.Equal(100m, r.OwnerOccupiedPercent + r.RenterOccupiedPercent));
    }
}
