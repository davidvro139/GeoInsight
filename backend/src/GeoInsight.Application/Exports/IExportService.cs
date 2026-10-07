using GeoInsight.Application.Common;

namespace GeoInsight.Application.Exports;

public interface IExportService
{
    Task<ServiceResult<ExportDto>> CreateAsync(
        long organizationId, long datasetId, long requestingUserId, CreateExportRequest request, CancellationToken cancellationToken);

    Task<ServiceResult<IReadOnlyList<ExportDto>>> GetAllAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<ExportFileDto>> DownloadAsync(
        long organizationId, long datasetId, long exportId, long requestingUserId, CancellationToken cancellationToken);
}
