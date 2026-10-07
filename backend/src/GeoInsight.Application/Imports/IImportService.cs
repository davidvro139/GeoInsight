using GeoInsight.Application.Common;

namespace GeoInsight.Application.Imports;

public interface IImportService
{
    Task<ServiceResult<ImportUploadResult>> UploadAsync(
        long organizationId,
        long datasetId,
        long requestingUserId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken);

    Task<ServiceResult<ImportSummaryDto>> ValidateAsync(
        long organizationId,
        long datasetId,
        long importId,
        long requestingUserId,
        ImportValidateRequest request,
        CancellationToken cancellationToken);

    Task<ServiceResult<ImportSummaryDto>> CommitAsync(
        long organizationId,
        long datasetId,
        long importId,
        long requestingUserId,
        ImportValidateRequest request,
        CancellationToken cancellationToken);

    Task<ServiceResult<IReadOnlyList<ImportListItemDto>>> GetAllAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken);
}
