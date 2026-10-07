using GeoInsight.Application.Tracts;

namespace GeoInsight.Infrastructure.Tracts;

/// <summary>
/// Stand-in for a real Census ACS Data API integration — that API requires a
/// free but still-required API key (confirmed live: api.census.gov returns
/// X-DataWebAPI-KeyError without one) that this project doesn't have
/// configured, so this generates deterministic synthetic values instead.
/// Deterministic per GEOID (seeded by its hash) so the same tract always
/// gets the same numbers rather than re-randomizing on every import.
/// NOT real Census data — every UI surface showing these must say so.
/// </summary>
public class PlaceholderDemographicDataSource : IDemographicDataSource
{
    public Task<IReadOnlyList<ParsedDemographics>> GetDemographicsAsync(
        IReadOnlyList<string> geoids, string? apiKey, CancellationToken cancellationToken)
    {
        var results = geoids.Select(Generate).ToList();
        return Task.FromResult<IReadOnlyList<ParsedDemographics>>(results);
    }

    private static ParsedDemographics Generate(string geoid)
    {
        var random = new Random(geoid.GetHashCode());

        var ownerOccupiedPercent = Math.Round(40 + random.NextDouble() * 40, 3); // 40-80%

        return new ParsedDemographics(
            Geoid: geoid,
            Population: random.Next(1200, 8001),
            MedianHouseholdIncome: Math.Round((decimal)(35_000 + random.NextDouble() * 85_000), 2),
            PovertyRate: Math.Round((decimal)(3 + random.NextDouble() * 22), 3),
            MedianAge: Math.Round((decimal)(28 + random.NextDouble() * 27), 2),
            OwnerOccupiedPercent: (decimal)ownerOccupiedPercent,
            RenterOccupiedPercent: (decimal)(100 - ownerOccupiedPercent),
            MinorityPopulationPercent: Math.Round((decimal)(5 + random.NextDouble() * 55), 3));
    }
}
