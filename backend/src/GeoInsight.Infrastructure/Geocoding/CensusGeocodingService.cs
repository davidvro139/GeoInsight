using System.Text.Json;
using GeoInsight.Application.Geocoding;
using Microsoft.Extensions.Logging;

namespace GeoInsight.Infrastructure.Geocoding;

/// <summary>
/// Calls the real, free, no-API-key U.S. Census Geocoder
/// (geographies/address endpoint — one call returns both coordinates and
/// Census geography, so there's no separate Census geography call here).
/// Response shape confirmed by calling the live endpoint directly during
/// development — see backend/tests/GeoInsight.Tests/Fixtures/census-address-*.json.
/// </summary>
public class CensusGeocodingService(HttpClient httpClient, ILogger<CensusGeocodingService> logger) : IGeocodingService
{
    public async Task<GeocodeResult> GeocodeAsync(GeocodeQuery query, CancellationToken cancellationToken)
    {
        var street = string.IsNullOrWhiteSpace(query.AddressLine2)
            ? query.AddressLine1
            : $"{query.AddressLine1} {query.AddressLine2}";

        var requestUri = "geographies/address" +
            $"?street={Uri.EscapeDataString(street)}" +
            $"&city={Uri.EscapeDataString(query.City)}" +
            $"&state={Uri.EscapeDataString(query.State)}" +
            (string.IsNullOrWhiteSpace(query.PostalCode) ? "" : $"&zip={Uri.EscapeDataString(query.PostalCode)}") +
            "&benchmark=Public_AR_Current&vintage=Current_Current&layers=all&format=json";

        try
        {
            using var response = await httpClient.GetAsync(requestUri, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return ParseResult(document);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Census geocoder call failed for {Street}, {City}, {State}", street, query.City, query.State);
            return GeocodeResult.Error(ex.Message);
        }
    }

    internal static GeocodeResult ParseResult(JsonDocument document)
    {
        var matches = document.RootElement.GetProperty("result").GetProperty("addressMatches");

        if (matches.GetArrayLength() == 0)
        {
            return GeocodeResult.NoMatch();
        }

        if (matches.GetArrayLength() > 1)
        {
            var candidates = matches.EnumerateArray().Select(ParseMatch)
                .Select(m => new GeocodeCandidate(
                    m.MatchedAddress, m.Latitude, m.Longitude, m.StateFips, m.CountyFips, m.Tract, m.BlockGroup, m.Block, m.Geoid))
                .ToList();
            return GeocodeResult.AmbiguousMatch(candidates);
        }

        var single = ParseMatch(matches[0]);

        return new GeocodeResult(
            Success: true,
            Ambiguous: false,
            MatchedAddress: single.MatchedAddress,
            Latitude: single.Latitude,
            Longitude: single.Longitude,
            StateFips: single.StateFips,
            CountyFips: single.CountyFips,
            Tract: single.Tract,
            BlockGroup: single.BlockGroup,
            Block: single.Block,
            Geoid: single.Geoid,
            ErrorMessage: null);
    }

    private static ParsedMatch ParseMatch(JsonElement match)
    {
        var matchedAddress = match.GetProperty("matchedAddress").GetString();

        var coordinates = match.GetProperty("coordinates");
        var longitude = coordinates.GetProperty("x").GetDecimal();
        var latitude = coordinates.GetProperty("y").GetDecimal();

        string? stateFips = null, countyFips = null, tract = null, geoid = null, blockGroup = null, block = null;

        if (match.TryGetProperty("geographies", out var geographies))
        {
            if (geographies.TryGetProperty("Census Tracts", out var tracts) && tracts.GetArrayLength() > 0)
            {
                var tractFeature = tracts[0];
                stateFips = tractFeature.GetProperty("STATE").GetString();
                countyFips = tractFeature.GetProperty("COUNTY").GetString();
                tract = tractFeature.GetProperty("TRACT").GetString();
                geoid = tractFeature.GetProperty("GEOID").GetString();
            }

            // The blocks layer name is vintage-year-prefixed (e.g. "2020 Census
            // Blocks") and will change — find it by substring instead of a
            // hardcoded literal. Exclude "Census Block Groups", a separate layer.
            var blocksLayerName = geographies.EnumerateObject()
                .Select(p => p.Name)
                .FirstOrDefault(name => name.Contains("Census Blocks", StringComparison.OrdinalIgnoreCase));

            if (blocksLayerName is not null
                && geographies.GetProperty(blocksLayerName).GetArrayLength() > 0)
            {
                var blockFeature = geographies.GetProperty(blocksLayerName)[0];
                blockGroup = blockFeature.GetProperty("BLKGRP").GetString();
                block = blockFeature.GetProperty("BLOCK").GetString();
            }
        }

        return new ParsedMatch(matchedAddress, latitude, longitude, stateFips, countyFips, tract, blockGroup, block, geoid);
    }

    private record ParsedMatch(
        string? MatchedAddress,
        decimal Latitude,
        decimal Longitude,
        string? StateFips,
        string? CountyFips,
        string? Tract,
        string? BlockGroup,
        string? Block,
        string? Geoid);
}
