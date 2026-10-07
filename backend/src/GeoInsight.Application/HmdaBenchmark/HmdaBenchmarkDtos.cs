namespace GeoInsight.Application.HmdaBenchmark;

/// <summary>Real dollars — the CFPB API's own "sum" field is already in dollars, not thousands.</summary>
public record HmdaCountyAggregate(long Count, decimal TotalAmount);

public record HmdaCountyBenchmarkDto(
    string StateFips,
    string CountyFips,
    int DatasetRecordCount,
    decimal DatasetTotalAmount,
    long? MarketOriginatedLoanCount,
    decimal? MarketTotalAmount,
    decimal? DatasetSharePercent);
