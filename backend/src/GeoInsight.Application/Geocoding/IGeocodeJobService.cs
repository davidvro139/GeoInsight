using GeoInsight.Application.Common;

namespace GeoInsight.Application.Geocoding;

public interface IGeocodeJobService
{
    Task<ServiceResult<GeocodeJobDto>> CreateAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<GeocodeJobDto>> GetAsync(
        long organizationId, long datasetId, long jobId, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<IReadOnlyList<GeocodeJobDto>>> GetAllAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<bool>> CancelAsync(
        long organizationId, long datasetId, long jobId, long requestingUserId, CancellationToken cancellationToken);
}
