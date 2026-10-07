namespace GeoInsight.Application.Common;

/// <summary>
/// One filter shape reused by every dataset-record read path (paged table,
/// map points, tract choropleth) so "filtering changes all visualizations
/// consistently" holds by construction — there's no second place a filter
/// could be implemented differently. All fields optional/additive (AND'd
/// together); a field left null/blank imposes no constraint.
/// </summary>
public record RecordFilterRequest(
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    string? Category = null,
    decimal? MinAmount = null,
    decimal? MaxAmount = null,
    string? City = null,
    string? State = null,
    string? CountyFips = null,
    string? Tract = null,
    string? GeocodeStatus = null,
    decimal? MinMedianIncome = null,
    decimal? MaxMedianIncome = null);
