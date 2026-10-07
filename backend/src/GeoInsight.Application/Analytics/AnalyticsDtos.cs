using GeoInsight.Domain.Entities;

namespace GeoInsight.Application.Analytics;

public record AnalyticsSummaryDto(
    int RecordCount,
    int GeocodedCount,
    decimal GeocodedRate,
    int FailedCount,
    decimal TotalAmount,
    int TractCount,
    int CountyCount,
    bool DemographicsAreEstimated,
    bool HasAssessmentArea);

public record CategoryCountDto(string? Category, int Count);

public record CountyCountDto(string CountyFips, int Count);

public record IncomeRangeCountDto(string Range, int Count);

public record GeocodeStatusCountDto(string Status, int Count);

public record TimeSeriesPointDto(string Period, int Count);

public record TractStatisticDto(
    string Geoid,
    string? TractName,
    string CountyFips,
    int? Population,
    int RecordCount,
    decimal? RecordsPer1000,
    decimal TotalAmount,
    decimal AverageAmount,
    decimal? CountyAverageRecordsPer1000,
    decimal? RegionAverageRecordsPer1000,
    decimal? DeviationFromCountyAveragePercent,
    TractIncomeTier? IncomeTier,
    bool? InAssessmentArea);

public record AnalyticsDto(
    AnalyticsSummaryDto Summary,
    IReadOnlyList<CategoryCountDto> ByCategory,
    IReadOnlyList<CountyCountDto> ByCounty,
    IReadOnlyList<IncomeRangeCountDto> ByIncomeRange,
    IReadOnlyList<GeocodeStatusCountDto> ByGeocodeStatus,
    IReadOnlyList<TimeSeriesPointDto> OverTime,
    IReadOnlyList<TractStatisticDto> TractStatistics);
