using GeoInsight.Application.Common;

namespace GeoInsight.Application.Datasets;

public interface IDatasetService
{
    Task<ServiceResult<DatasetDetailDto>> CreateAsync(
        long organizationId, long requestingUserId, CreateDatasetRequest request, CancellationToken cancellationToken);

    Task<ServiceResult<IReadOnlyList<DatasetSummaryDto>>> GetAllAsync(
        long organizationId, long requestingUserId, bool includeArchived, CancellationToken cancellationToken);

    Task<ServiceResult<DatasetDetailDto>> GetByIdAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<DatasetDetailDto>> UpdateAsync(
        long organizationId,
        long datasetId,
        long requestingUserId,
        UpdateDatasetRequest request,
        CancellationToken cancellationToken);

    Task<ServiceResult<bool>> ArchiveAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<bool>> UnarchiveAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken);
}
