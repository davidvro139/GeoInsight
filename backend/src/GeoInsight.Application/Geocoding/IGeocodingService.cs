namespace GeoInsight.Application.Geocoding;

/// <summary>
/// Wraps the U.S. Census Geocoder so no controller/service talks HTTP to it
/// directly (07_CLAUDE_MASTER_PROMPT.md) — swap the implementation to add
/// another provider later without touching callers.
/// </summary>
public interface IGeocodingService
{
    Task<GeocodeResult> GeocodeAsync(GeocodeQuery query, CancellationToken cancellationToken);
}
