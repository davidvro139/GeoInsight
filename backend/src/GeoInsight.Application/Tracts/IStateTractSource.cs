namespace GeoInsight.Application.Tracts;

/// <summary>
/// Returns raw parsed tract features for a whole state (TIGER doesn't offer
/// per-county downloads) — the caller filters down to the counties it needs.
/// The real implementation downloads and parses a live Census TIGER/Line
/// shapefile; tests substitute a fake with a couple of hand-built polygons.
/// </summary>
public interface IStateTractSource
{
    Task<IReadOnlyList<ParsedTractFeature>> GetTractsAsync(string stateFips, CancellationToken cancellationToken);
}
