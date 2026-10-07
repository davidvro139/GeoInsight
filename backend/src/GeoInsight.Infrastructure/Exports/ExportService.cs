using GeoInsight.Application.Common;
using GeoInsight.Application.Exports;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using GeoInsight.Infrastructure.Records;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.Exports;

/// <summary>
/// Csv/Xlsx/GeoJson are generated here from the dataset's own filtered
/// records via the Phase 9 RecordFilterQueryExtensions.ApplyFilters — the
/// same shared filter path every other read endpoint uses, so "exports use
/// current filters" holds structurally. Png/Pdf only exist as rendered
/// pixels in the browser (MapLibre/Chart.js canvases), so the client renders
/// them and this service just persists the given bytes.
/// </summary>
public class ExportService(
    GeoInsightDbContext dbContext, IDatasetAccessControl accessControl, IFileStorageService fileStorage, IAuditService auditService)
    : IExportService
{
    private static readonly Dictionary<ExportType, string> Extensions = new()
    {
        [ExportType.Csv] = "csv",
        [ExportType.Xlsx] = "xlsx",
        [ExportType.GeoJson] = "geojson",
        [ExportType.Png] = "png",
        [ExportType.Pdf] = "pdf",
    };

    private static readonly Dictionary<ExportType, string> ContentTypes = new()
    {
        [ExportType.Csv] = "text/csv",
        [ExportType.Xlsx] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [ExportType.GeoJson] = "application/geo+json",
        [ExportType.Png] = "image/png",
        [ExportType.Pdf] = "application/pdf",
    };

    public async Task<ServiceResult<ExportDto>> CreateAsync(
        long organizationId, long datasetId, long requestingUserId, CreateExportRequest request, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Analyst, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<ExportDto>.Failure(accessError.Type, accessError.Message);
        }

        string storedFileName;
        string? filtersJson = null;

        if (request.ExportType is ExportType.Png or ExportType.Pdf)
        {
            if (string.IsNullOrEmpty(request.FileContentBase64))
            {
                return ServiceResult<ExportDto>.Failure(ServiceErrorType.Validation, "FileContentBase64 is required for this export type.");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(request.FileContentBase64);
            }
            catch (FormatException)
            {
                return ServiceResult<ExportDto>.Failure(ServiceErrorType.Validation, "FileContentBase64 is not valid base64.");
            }

            await using var contentStream = new MemoryStream(bytes);
            storedFileName = await fileStorage.SaveAsync(contentStream, $"export.{Extensions[request.ExportType]}", cancellationToken);
        }
        else
        {
            var filter = request.Filters ?? new RecordFilterRequest();
            filtersJson = System.Text.Json.JsonSerializer.Serialize(filter);

            var records = await dbContext.DatasetRecords
                .Where(r => r.DatasetId == datasetId)
                .ApplyFilters(filter, dbContext)
                .ToListAsync(cancellationToken);

            var geoids = records.Where(r => r.CensusGeoid != null).Select(r => r.CensusGeoid!).Distinct().ToList();
            var demographicsByGeoid = await dbContext.CensusDemographics
                .Where(d => geoids.Contains(d.CensusGeoid))
                .ToDictionaryAsync(d => d.CensusGeoid, cancellationToken);

            await using var contentStream = request.ExportType switch
            {
                ExportType.Csv => await CsvExportWriter.WriteAsync(records, demographicsByGeoid, cancellationToken),
                ExportType.Xlsx => XlsxExportWriter.Write(records, demographicsByGeoid),
                ExportType.GeoJson => GeoJsonExportWriter.Write(records, demographicsByGeoid),
                _ => throw new InvalidOperationException($"Unhandled export type: {request.ExportType}"),
            };
            storedFileName = await fileStorage.SaveAsync(contentStream, $"export.{Extensions[request.ExportType]}", cancellationToken);
        }

        var now = DateTime.UtcNow;
        var export = new Export
        {
            OrganizationId = organizationId,
            DatasetId = datasetId,
            UserId = requestingUserId,
            ExportType = request.ExportType,
            Status = ExportStatus.Completed,
            FilePath = storedFileName,
            Filters = filtersJson,
            CreatedAt = now,
            CompletedAt = now,
        };
        dbContext.Exports.Add(export);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Logged after the insert (not batched into it) — the auto-increment
        // Id doesn't exist until SaveChangesAsync assigns it. Also covers the
        // PRD's "report generation" action: the PDF report POSTs here as a
        // plain Pdf export, so there's no separate call site for it.
        auditService.Log(organizationId, requestingUserId, "export.create", "export", export.Id.ToString(),
            new { datasetId, exportType = export.ExportType.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<ExportDto>.Success(ToDto(export));
    }

    public async Task<ServiceResult<IReadOnlyList<ExportDto>>> GetAllAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<IReadOnlyList<ExportDto>>.Failure(accessError.Type, accessError.Message);
        }

        var exports = await dbContext.Exports
            .Where(e => e.DatasetId == datasetId && e.OrganizationId == organizationId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<ExportDto>>.Success(exports.Select(ToDto).ToList());
    }

    public async Task<ServiceResult<ExportFileDto>> DownloadAsync(
        long organizationId, long datasetId, long exportId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<ExportFileDto>.Failure(accessError.Type, accessError.Message);
        }

        var export = await dbContext.Exports.SingleOrDefaultAsync(
            e => e.Id == exportId && e.DatasetId == datasetId && e.OrganizationId == organizationId, cancellationToken);
        if (export is null || export.FilePath is null)
        {
            return ServiceResult<ExportFileDto>.Failure(ServiceErrorType.NotFound, "Export not found.");
        }

        var datasetName = await dbContext.Datasets
            .Where(d => d.Id == datasetId)
            .Select(d => d.Name)
            .SingleAsync(cancellationToken);

        var extension = Extensions[export.ExportType];
        var fileName = $"{datasetName}-{export.CreatedAt:yyyyMMdd-HHmmss}.{extension}";
        var content = fileStorage.OpenRead(export.FilePath);

        return ServiceResult<ExportFileDto>.Success(new ExportFileDto(content, fileName, ContentTypes[export.ExportType]));
    }

    private static ExportDto ToDto(Export export) => new(
        export.Id, export.ExportType.ToString(), export.Status.ToString(), export.CreatedAt, export.CompletedAt);
}
