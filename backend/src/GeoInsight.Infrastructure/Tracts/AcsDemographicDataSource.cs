using System.Text.Json;
using GeoInsight.Application.Tracts;
using Microsoft.Extensions.Logging;

namespace GeoInsight.Infrastructure.Tracts;

/// <summary>
/// Calls the real Census ACS 5-Year Data API (api.census.gov/data/2022/acs/acs5).
/// Unlike the Geocoder/TIGER services this app otherwise uses, this one
/// requires a caller-supplied API key (free signup, not built into this
/// project) — see IDemographicDataSource's doc comment. Queried per
/// state+county (the API's own natural grouping — "give me every tract in
/// this county"), then filtered down to just the requested GEOIDs, since a
/// dataset rarely needs every tract in a county.
///
/// Variable codes (stable, long-standing ACS detail-table variables):
/// B01003_001E total population; B19013_001E median household income;
/// B17001_002E/B17001_001E poverty count/universe; B01002_001E median age;
/// B25003_002E/003E/001E owner/renter/total occupied housing units;
/// B03002_001E/003E total population/white-alone-non-Hispanic population
/// (minority % = 100 - white-alone-non-Hispanic share).
///
/// Throws on any failure (bad key, network error, unexpected shape) rather
/// than returning a partial/error result — TractImportRunner catches this and
/// falls back to the placeholder source, since a demographics-source outage
/// is an infrastructure failure, not a per-record outcome the caller should
/// have to unpack (unlike geocoding, where "no match" is an expected result).
/// </summary>
public class AcsDemographicDataSource(HttpClient httpClient, ILogger<AcsDemographicDataSource> logger) : IAcsDemographicDataSource
{
    private const string Variables = "NAME,B01003_001E,B19013_001E,B17001_002E,B17001_001E,B01002_001E,B25003_002E,B25003_003E,B25003_001E,B03002_001E,B03002_003E";

    public async Task<IReadOnlyList<ParsedDemographics>> GetDemographicsAsync(
        IReadOnlyList<string> geoids, string? apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("An API key is required to call the real Census ACS Data API.");
        }

        var requestedGeoids = geoids.ToHashSet();
        var countyGroups = geoids
            .Select(g => (Geoid: g, StateFips: g[..2], CountyFips: g.Substring(2, 3)))
            .GroupBy(g => (g.StateFips, g.CountyFips));

        var results = new List<ParsedDemographics>();

        foreach (var group in countyGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var requestUri = "data/2022/acs/acs5" +
                $"?get={Variables}" +
                "&for=tract:*" +
                $"&in=state:{group.Key.StateFips}+county:{group.Key.CountyFips}" +
                $"&key={Uri.EscapeDataString(apiKey)}";

            using var response = await httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Census ACS API returned {(int)response.StatusCode} for state {group.Key.StateFips} county {group.Key.CountyFips}: {body}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var parsed = ParseCountyResponse(document, requestedGeoids);
            results.AddRange(parsed);

            logger.LogInformation(
                "Fetched real ACS demographics for {Count} tracts in state {State} county {County}",
                parsed.Count, group.Key.StateFips, group.Key.CountyFips);
        }

        return results;
    }

    /// <summary>
    /// Parses one county's worth of ACS response rows (a JSON array of arrays;
    /// the first row is the header) down to just the requested GEOIDs.
    /// Extracted as its own static method so tests can exercise real parsing
    /// logic against a fixture without a live HttpClient — same approach as
    /// CensusGeocodingService.ParseResult.
    /// </summary>
    internal static List<ParsedDemographics> ParseCountyResponse(JsonDocument document, HashSet<string> requestedGeoids)
    {
        var results = new List<ParsedDemographics>();
        var rows = document.RootElement.EnumerateArray().ToList();
        if (rows.Count == 0)
        {
            return results;
        }

        var header = rows[0].EnumerateArray().Select(e => e.GetString()!).ToList();
        int Col(string name) => header.IndexOf(name);

        var stateIdx = Col("state");
        var countyIdx = Col("county");
        var tractIdx = Col("tract");
        var popIdx = Col("B01003_001E");
        var incomeIdx = Col("B19013_001E");
        var povertyCountIdx = Col("B17001_002E");
        var povertyUniverseIdx = Col("B17001_001E");
        var ageIdx = Col("B01002_001E");
        var ownerIdx = Col("B25003_002E");
        var renterIdx = Col("B25003_003E");
        var occupiedTotalIdx = Col("B25003_001E");
        var popForMinorityIdx = Col("B03002_001E");
        var whiteNonHispanicIdx = Col("B03002_003E");

        foreach (var row in rows.Skip(1))
        {
            var cells = row.EnumerateArray().Select(e => e.GetString()).ToList();
            var geoid = cells[stateIdx] + cells[countyIdx] + cells[tractIdx];
            if (!requestedGeoids.Contains(geoid))
            {
                continue;
            }

            decimal? Num(int idx)
            {
                if (!decimal.TryParse(cells[idx], out var value))
                {
                    return null;
                }
                // Census uses large negative sentinels (e.g. -666666666) for
                // suppressed/unavailable estimates — never a real value here.
                return value < 0 ? null : value;
            }

            var population = Num(popIdx);
            var povertyCount = Num(povertyCountIdx);
            var povertyUniverse = Num(povertyUniverseIdx);
            var owner = Num(ownerIdx);
            var renter = Num(renterIdx);
            var occupiedTotal = Num(occupiedTotalIdx);
            var totalForMinority = Num(popForMinorityIdx);
            var whiteNonHispanic = Num(whiteNonHispanicIdx);

            results.Add(new ParsedDemographics(
                Geoid: geoid,
                Population: (int)(population ?? 0),
                MedianHouseholdIncome: Num(incomeIdx) ?? 0,
                PovertyRate: povertyUniverse is > 0 ? Math.Round(povertyCount!.Value / povertyUniverse.Value * 100, 3) : 0,
                MedianAge: Num(ageIdx) ?? 0,
                OwnerOccupiedPercent: occupiedTotal is > 0 ? Math.Round(owner!.Value / occupiedTotal.Value * 100, 3) : 0,
                RenterOccupiedPercent: occupiedTotal is > 0 ? Math.Round(renter!.Value / occupiedTotal.Value * 100, 3) : 0,
                MinorityPopulationPercent: totalForMinority is > 0
                    ? Math.Round(100 - (whiteNonHispanic ?? 0) / totalForMinority.Value * 100, 3)
                    : 0,
                RawJson: JsonSerializer.Serialize(header.Zip(cells).ToDictionary(p => p.First, p => p.Second))));
        }

        return results;
    }
}
