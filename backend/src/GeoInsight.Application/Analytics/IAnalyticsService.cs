using GeoInsight.Application.Common;

namespace GeoInsight.Application.Analytics;

public interface IAnalyticsService
{
    Task<ServiceResult<AnalyticsDto>> GetAnalyticsAsync(
        long organizationId, long datasetId, long requestingUserId, RecordFilterRequest filter, CancellationToken cancellationToken);
}
