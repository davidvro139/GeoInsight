using NetTopologySuite.Geometries;

namespace GeoInsight.Domain.Entities;

/// <summary>
/// Global reference data, not scoped to any organization/dataset — imported
/// on-demand (Phase 7) for whichever counties a dataset's geocoded records
/// actually fall in, keyed by GEOID so re-importing an already-known tract is
/// a no-op.
/// </summary>
public class CensusTract
{
    public long Id { get; set; }

    public required string Geoid { get; set; }

    public required string StateFips { get; set; }

    public required string CountyFips { get; set; }

    public required string TractCode { get; set; }

    public string? StateName { get; set; }

    /// <summary>e.g. "Census Tract 6.02" (TIGER's NAMELSAD field). No county
    /// name is available from the tract shapefile itself.</summary>
    public string? TractName { get; set; }

    /// <summary>Always a MultiPolygon, SRID 4326 — TIGER ships single Polygons,
    /// normalized to MultiPolygon at import time to match the schema.</summary>
    public required Geometry Boundary { get; set; }
}
