using System.Text.Json;
using GeoInsight.Infrastructure.Geocoding;

namespace GeoInsight.Tests;

/// <summary>
/// Exercises the real response shape from the live U.S. Census Geocoder,
/// captured into a fixture file during development (see
/// Fixtures/census-address-*.json) rather than calling the live API from the
/// test suite — same "verify real shape once, test against a fixture after"
/// approach used for the multipart upload boundary in ImportsApiTests.
/// </summary>
public class CensusGeocodingServiceTests
{
    private static JsonDocument LoadFixture(string fileName) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine("Fixtures", fileName)));

    [Fact]
    public void A_single_match_extracts_coordinates_and_census_geography()
    {
        using var document = LoadFixture("census-address-match.json");

        var result = CensusGeocodingService.ParseResult(document);

        Assert.True(result.Success);
        Assert.False(result.Ambiguous);
        Assert.Equal("4600 SILVER HILL RD, WASHINGTON, DC, 20233", result.MatchedAddress);
        Assert.Equal(38.845053106269m, result.Latitude);
        Assert.Equal(-76.928365658124m, result.Longitude);
        Assert.Equal("24", result.StateFips);
        Assert.Equal("033", result.CountyFips);
        Assert.Equal("802405", result.Tract);
        Assert.Equal("24033802405", result.Geoid);
        Assert.Equal("2", result.BlockGroup);
        Assert.Equal("2004", result.Block);
    }

    [Fact]
    public void Multiple_matches_are_reported_as_ambiguous_with_all_candidates()
    {
        using var document = LoadFixture("census-address-ambiguous.json");

        var result = CensusGeocodingService.ParseResult(document);

        Assert.True(result.Success);
        Assert.True(result.Ambiguous);
        Assert.Null(result.Latitude);
        Assert.NotNull(result.Candidates);
        Assert.Equal(2, result.Candidates!.Count);

        var first = result.Candidates[0];
        Assert.Equal("100 SW MAIN ST, PORTLAND, OR, 97204", first.MatchedAddress);
        Assert.Equal(45.515048316843m, first.Latitude);
        Assert.Equal(-122.675175337882m, first.Longitude);
        Assert.Equal("41", first.StateFips);
        Assert.Equal("051", first.CountyFips);
        Assert.Equal("010602", first.Tract);
        Assert.Equal("41051010602", first.Geoid);
        Assert.Equal("2", first.BlockGroup);
        Assert.Equal("2067", first.Block);

        var second = result.Candidates[1];
        Assert.Equal("100 SE MAIN ST, PORTLAND, OR, 97214", second.MatchedAddress);
        Assert.Equal("41051001101", second.Geoid);
    }

    [Fact]
    public void No_matches_is_reported_as_a_failure_not_an_exception()
    {
        using var document = LoadFixture("census-address-nomatch.json");

        var result = CensusGeocodingService.ParseResult(document);

        Assert.False(result.Success);
        Assert.False(result.Ambiguous);
        Assert.NotNull(result.ErrorMessage);
    }
}
