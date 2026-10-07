namespace GeoInsight.Application.Geocoding;

public record GeocodeQuery(string AddressLine1, string? AddressLine2, string City, string State, string? PostalCode);

/// <summary>One candidate from a 2+-match Census response — stored verbatim on
/// the record while it's "ambiguous" so the Address Review Queue (Phase 6) can
/// list them and let a human accept one.</summary>
public record GeocodeCandidate(
    string? MatchedAddress,
    decimal Latitude,
    decimal Longitude,
    string? StateFips,
    string? CountyFips,
    string? Tract,
    string? BlockGroup,
    string? Block,
    string? Geoid);

/// <summary>
/// Success + Ambiguous=false -> exactly one Census match (coordinates/geography
/// populated). Success + Ambiguous=true -> 2+ matches, nothing applied,
/// Candidates holds all of them for the Address Review Queue to resolve.
/// Success=false -> no match, or a network/parse failure (ErrorMessage set).
/// </summary>
public record GeocodeResult(
    bool Success,
    bool Ambiguous,
    string? MatchedAddress,
    decimal? Latitude,
    decimal? Longitude,
    string? StateFips,
    string? CountyFips,
    string? Tract,
    string? BlockGroup,
    string? Block,
    string? Geoid,
    string? ErrorMessage,
    IReadOnlyList<GeocodeCandidate>? Candidates = null)
{
    public static GeocodeResult NoMatch() => new(false, false, null, null, null, null, null, null, null, null, null, "No address match found.");

    public static GeocodeResult Error(string message) => new(false, false, null, null, null, null, null, null, null, null, null, message);

    public static GeocodeResult AmbiguousMatch(IReadOnlyList<GeocodeCandidate> candidates) =>
        new(true, true, null, null, null, null, null, null, null, null, null, null, candidates);
}
