namespace GeoInsight.Application.Tracts;

public record ParsedDemographics(
    string Geoid,
    int Population,
    decimal MedianHouseholdIncome,
    decimal PovertyRate,
    decimal MedianAge,
    decimal OwnerOccupiedPercent,
    decimal RenterOccupiedPercent,
    decimal MinorityPopulationPercent,
    string? RawJson = null);

/// <summary>
/// Returns demographic figures for a batch of tract GEOIDs.
/// PlaceholderDemographicDataSource generates deterministic synthetic values
/// and ignores apiKey; AcsDemographicDataSource calls the real Census ACS
/// Data API (which requires the caller to supply a free API key) and returns
/// RawJson populated so callers can tell real data from synthetic.
/// </summary>
public interface IDemographicDataSource
{
    Task<IReadOnlyList<ParsedDemographics>> GetDemographicsAsync(
        IReadOnlyList<string> geoids, string? apiKey, CancellationToken cancellationToken);
}

/// <summary>
/// Marker interface so DI can hand TractImportRunner the real ACS source and
/// the synthetic placeholder as two independently resolvable dependencies
/// (rather than one IDemographicDataSource registration it would have to pick
/// apart at runtime) — keeps both swappable/mockable in tests.
/// </summary>
public interface IAcsDemographicDataSource : IDemographicDataSource;
