using GeoInsight.Application.Common;
using GeoInsight.Application.Datasets;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.Datasets;

public class DatasetService(GeoInsightDbContext dbContext, IOrganizationAccessControl accessControl, IAuditService auditService) : IDatasetService
{
    public async Task<ServiceResult<DatasetDetailDto>> CreateAsync(
        long organizationId, long requestingUserId, CreateDatasetRequest request, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Analyst, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<DatasetDetailDto>.Failure(accessError.Type, accessError.Message);
        }

        var now = DateTime.UtcNow;
        var dataset = new Dataset
        {
            OrganizationId = organizationId,
            Name = request.Name,
            Description = request.Description,
            DatasetType = string.IsNullOrWhiteSpace(request.DatasetType) ? "generic" : request.DatasetType,
            CreatedByUserId = requestingUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Datasets.Add(dataset);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Logged after the insert (not batched into it) — the auto-increment
        // Id doesn't exist until SaveChangesAsync assigns it.
        auditService.Log(organizationId, requestingUserId, "dataset.create", "dataset", dataset.Id.ToString(),
            new { name = dataset.Name, datasetType = dataset.DatasetType });
        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(organizationId, dataset.Id, requestingUserId, cancellationToken);
    }

    public async Task<ServiceResult<IReadOnlyList<DatasetSummaryDto>>> GetAllAsync(
        long organizationId, long requestingUserId, bool includeArchived, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<IReadOnlyList<DatasetSummaryDto>>.Failure(accessError.Type, accessError.Message);
        }

        var datasets = await dbContext.Datasets
            .Where(d => d.OrganizationId == organizationId && (includeArchived || !d.IsArchived))
            .OrderBy(d => d.Name)
            .Select(d => new DatasetSummaryDto(
                d.Id,
                d.Name,
                d.DatasetType,
                d.IsArchived,
                d.UpdatedAt,
                dbContext.DatasetRecords.Count(r => r.DatasetId == d.Id),
                dbContext.DatasetRecords.Count(r => r.DatasetId == d.Id && (r.GeocodeStatus == "matched" || r.GeocodeStatus == "manually_resolved"))))
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<DatasetSummaryDto>>.Success(datasets);
    }

    public async Task<ServiceResult<DatasetDetailDto>> GetByIdAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<DatasetDetailDto>.Failure(accessError.Type, accessError.Message);
        }

        var dto = await (
            from d in dbContext.Datasets
            join u in dbContext.Users on d.CreatedByUserId equals u.Id
            where d.Id == datasetId && d.OrganizationId == organizationId
            select new DatasetDetailDto(
                d.Id, d.Name, d.Description, d.DatasetType, d.IsArchived, d.CreatedAt, d.UpdatedAt, u.DisplayName)
        ).SingleOrDefaultAsync(cancellationToken);

        return dto is null
            ? ServiceResult<DatasetDetailDto>.Failure(ServiceErrorType.NotFound, "Dataset not found.")
            : ServiceResult<DatasetDetailDto>.Success(dto);
    }

    public async Task<ServiceResult<DatasetDetailDto>> UpdateAsync(
        long organizationId,
        long datasetId,
        long requestingUserId,
        UpdateDatasetRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Analyst, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<DatasetDetailDto>.Failure(accessError.Type, accessError.Message);
        }

        var dataset = await GetOrgScopedDatasetAsync(organizationId, datasetId, cancellationToken);
        if (dataset is null)
        {
            return ServiceResult<DatasetDetailDto>.Failure(ServiceErrorType.NotFound, "Dataset not found.");
        }

        dataset.Name = request.Name;
        dataset.Description = request.Description;
        dataset.DatasetType = string.IsNullOrWhiteSpace(request.DatasetType) ? "generic" : request.DatasetType;
        dataset.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(organizationId, datasetId, requestingUserId, cancellationToken);
    }

    public async Task<ServiceResult<bool>> ArchiveAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken) =>
        await SetArchivedAsync(organizationId, datasetId, requestingUserId, isArchived: true, cancellationToken);

    public async Task<ServiceResult<bool>> UnarchiveAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken) =>
        await SetArchivedAsync(organizationId, datasetId, requestingUserId, isArchived: false, cancellationToken);

    private async Task<ServiceResult<bool>> SetArchivedAsync(
        long organizationId, long datasetId, long requestingUserId, bool isArchived, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Analyst, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<bool>.Failure(accessError.Type, accessError.Message);
        }

        var dataset = await GetOrgScopedDatasetAsync(organizationId, datasetId, cancellationToken);
        if (dataset is null)
        {
            return ServiceResult<bool>.Failure(ServiceErrorType.NotFound, "Dataset not found.");
        }

        dataset.IsArchived = isArchived;
        dataset.ArchivedAt = isArchived ? DateTime.UtcNow : null;
        dataset.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Success(true);
    }

    private Task<Dataset?> GetOrgScopedDatasetAsync(long organizationId, long datasetId, CancellationToken cancellationToken) =>
        dbContext.Datasets.SingleOrDefaultAsync(
            d => d.Id == datasetId && d.OrganizationId == organizationId, cancellationToken);
}
