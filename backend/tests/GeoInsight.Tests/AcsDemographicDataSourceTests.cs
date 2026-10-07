using System.Text.Json;
using GeoInsight.Infrastructure.Tracts;

namespace GeoInsight.Tests;

/// <summary>
/// Exercises ParseCountyResponse against a fixture. Unlike the Census
/// Geocoder fixtures (captured from real live calls), this fixture is
/// hand-built to match the ACS 5-Year Data API's real, well-documented
/// response shape and variable codes — no free API key was available in this
/// environment to capture a genuine live response. The parsing logic itself
/// (column lookup by header name, GEOID assembly, negative-sentinel handling)
/// only depends on that documented shape being accurate, not on any
/// particular tract's real values.
/// </summary>
public class AcsDemographicDataSourceTests
{
    private static JsonDocument LoadFixture() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine("Fixtures", "acs-demographics-response.json")));

    [Fact]
    public void Parses_population_income_and_percentages_from_the_response()
    {
        using var document = LoadFixture();
        var requested = new HashSet<string> { "49057001100", "49057001200" };

        var results = AcsDemographicDataSource.ParseCountyResponse(document, requested);

        var tract1 = results.Single(r => r.Geoid == "49057001100");
        Assert.Equal(6000, tract1.Population);
        Assert.Equal(65_000m, tract1.MedianHouseholdIncome);
        Assert.Equal(10m, tract1.PovertyRate); // 600/6000*100
        Assert.Equal(35m, tract1.MedianAge);
        Assert.Equal(75m, tract1.OwnerOccupiedPercent); // 1800/2400*100
        Assert.Equal(25m, tract1.RenterOccupiedPercent); // 600/2400*100
        Assert.Equal(33.333m, tract1.MinorityPopulationPercent); // 100 - 4000/6000*100
        Assert.NotNull(tract1.RawJson);
    }

    [Fact]
    public void Negative_sentinel_values_are_treated_as_null_not_as_real_data()
    {
        using var document = LoadFixture();
        var requested = new HashSet<string> { "49057001100", "49057001200" };

        var results = AcsDemographicDataSource.ParseCountyResponse(document, requested);

        var tract2 = results.Single(r => r.Geoid == "49057001200");
        Assert.Equal(0m, tract2.MedianHouseholdIncome); // -666666666 sentinel -> null -> defaulted to 0
        Assert.Equal(0m, tract2.PovertyRate); // poverty universe is 0, not negative, but still not > 0
        Assert.Equal(100m, tract2.MinorityPopulationPercent); // white-alone-non-Hispanic sentinel -> treated as 0
    }

    [Fact]
    public void Rows_for_geoids_that_were_not_requested_are_excluded()
    {
        using var document = LoadFixture();
        // Fixture has a third tract (...001300) not included here.
        var requested = new HashSet<string> { "49057001100", "49057001200" };

        var results = AcsDemographicDataSource.ParseCountyResponse(document, requested);

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, r => r.Geoid == "49057001300");
    }
}
