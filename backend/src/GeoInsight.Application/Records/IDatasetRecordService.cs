using GeoInsight.Application.Common;

namespace GeoInsight.Application.Records;

public interface IDatasetRecordService
{
    Task<ServiceResult<IReadOnlyList<DatasetRecordPointDto>>> GetPointsAsync(
        long organizationId, long datasetId, long requestingUserId, RecordFilterRequest filter, CancellationToken cancellationToken);

    Task<ServiceResult<PagedResult<DatasetRecordListItemDto>>> GetPagedAsync(
        long organizationId,
        long datasetId,
        long requestingUserId,
        int page,
        int pageSize,
        string? sortBy,
        string? sortDir,
        RecordFilterRequest filter,
        CancellationToken cancellationToken);
}
