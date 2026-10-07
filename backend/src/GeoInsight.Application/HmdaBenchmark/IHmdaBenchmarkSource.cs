namespace GeoInsight.Application.HmdaBenchmark;

/// <summary>
/// Real, public HMDA mortgage-lending aggregate data from the CFPB's HMDA
/// Data Browser API (ffiec.cfpb.gov/v2/data-browser-api/view/aggregations) —
/// county-level only, that's a real constraint of the public API itself (no
/// tract-level filter exists), not a simplification made here. Returns null
/// on any failure (network error, unexpected shape, no data for that
/// county/year) rather than throwing — one flaky external county lookup
/// should never fail the whole benchmark.
/// </summary>
public interface IHmdaBenchmarkSource
{
    Task<HmdaCountyAggregate?> GetOriginatedLoanAggregateAsync(string countyFips, int year, CancellationToken cancellationToken);
}
