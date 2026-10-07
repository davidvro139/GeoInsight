using GeoInsight.Application.Common;

namespace GeoInsight.Application.SavedViews;

/// <summary>
/// The Map-page-only state Phases 9/10 introduced beyond RecordFilterRequest
/// (which chart/table/choropleth already share) — tract-boundary visibility
/// and the chosen choropleth metric. CenterLng/CenterLat/Zoom (Phase 15) capture
/// the map camera itself; all three are nullable so views saved before this
/// field existed still deserialize fine and just skip re-centering on open.
/// </summary>
public record MapConfigurationDto(
    bool ShowTracts,
    string? ChoroplethMetric,
    double? CenterLng = null,
    double? CenterLat = null,
    double? Zoom = null);

public record SavedViewDto(
    long Id,
    string Name,
    long UserId,
    string OwnerDisplayName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    RecordFilterRequest? Filters,
    MapConfigurationDto? MapConfiguration);

public record CreateSavedViewRequest(string Name, RecordFilterRequest? Filters, MapConfigurationDto? MapConfiguration);

public record RenameSavedViewRequest(string Name);
