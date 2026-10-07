using GeoInsight.Application.Common;

namespace GeoInsight.Application.HmdaBenchmark;

/// <summary>
/// Compares a dataset's own (filtered) county-level activity against the
/// real public HMDA mortgage-lending market in the same counties — a live
/// external lookup, unlike everything IAnalyticsService computes locally, so
/// it's kept as its own on-demand endpoint rather than folded into
/// GetAnalyticsAsync.
/// </summary>
public interface IHmdaBenchmarkService
{
    Task<ServiceResult<IReadOnlyList<HmdaCountyBenchmarkDto>>> GetBenchmarkAsync(
        long organizationId, long datasetId, long requestingUserId, int year, RecordFilterRequest filter,
        CancellationToken cancellationToken);
}
