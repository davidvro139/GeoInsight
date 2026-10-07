using GeoInsight.Domain.Entities;
using NetTopologySuite.Geometries;

namespace GeoInsight.Application.Tracts;

public record CountyRef(string StateFips, string CountyFips);

public record TractImportStatusDto(
    IReadOnlyList<CountyRef> NeededCounties,
    IReadOnlyList<CountyRef> ImportedCounties,
    bool AllImported,
    bool HasUnassignedCoordinates,
    bool HasEstimatedDemographics);

public record TractDemographicsDto(
    int? Population,
    decimal? MedianHouseholdIncome,
    decimal? PovertyRate,
    decimal? MedianAge,
    decimal? OwnerOccupiedPercent,
    decimal? RenterOccupiedPercent,
    decimal? MinorityPopulationPercent,
    int SurveyYear,
    bool IsEstimated);

public record TractIncomeLevelDto(
    TractIncomeTier Tier,
    decimal? TractMedianFamilyIncome,
    decimal? AreaMedianFamilyIncome,
    decimal? IncomePercentageOfAreaMedian,
    string? MsaMdName);

public record TractBoundaryDto(
    string Geoid, string? TractName, string CountyFips, Geometry Boundary, TractDemographicsDto? Demographics,
    TractIncomeLevelDto? IncomeLevel);

public record ParsedTractFeature(string Geoid, string CountyFips, string TractCode, string TractName, Geometry Boundary);

/// <summary>
/// Population/MedianHouseholdIncome/PovertyRate/MinorityPopulationPercent
/// come straight from census_demographics (untouched by any record filter —
/// a tract's population doesn't change because you filtered records).
/// RecordCount/TotalAmount/AverageAmount/RecordsPer1000 are computed from the
/// dataset's own (filtered) records grouped by CensusGeoid — this is the
/// metric family that visibly reacts to the active filter.
/// </summary>
public enum ChoroplethMetric
{
    Population,
    MedianHouseholdIncome,
    PovertyRate,
    MinorityPopulationPercent,
    RecordCount,
    TotalAmount,
    AverageAmount,
    RecordsPer1000,
    /// <summary>Ordinal 1-4 (Low..Upper), FFIEC's real LMI classification — see TractIncomeLevelDto.</summary>
    IncomeLevel,
}

public record TractMetricDto(string Geoid, decimal? Value);

public record ImportedCountyDto(string StateFips, string CountyFips, int TractCount);

public record RequestCountyImportRequest(string StateFips, string CountyFips);
