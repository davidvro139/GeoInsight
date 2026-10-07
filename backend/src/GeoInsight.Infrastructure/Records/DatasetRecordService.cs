using GeoInsight.Application.Common;
using GeoInsight.Application.Records;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.Records;

public class DatasetRecordService(GeoInsightDbContext dbContext, IDatasetAccessControl accessControl) : IDatasetRecordService
{
    private static readonly HashSet<string> SortableFields = ["createdat", "amount", "recorddate", "city", "externalid"];

    public async Task<ServiceResult<IReadOnlyList<DatasetRecordPointDto>>> GetPointsAsync(
        long organizationId, long datasetId, long requestingUserId, RecordFilterRequest filter, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<IReadOnlyList<DatasetRecordPointDto>>.Failure(accessError.Type, accessError.Message);
        }

        var query = dbContext.DatasetRecords
            .Where(r => r.DatasetId == datasetId && r.Latitude != null && r.Longitude != null)
            .ApplyFilters(filter, dbContext);

        var points = await query
            .Select(r => new DatasetRecordPointDto(
                r.Id, r.Latitude!.Value, r.Longitude!.Value, r.ExternalId, r.Category, r.CensusTract, r.CensusGeoid))
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<DatasetRecordPointDto>>.Success(points);
    }

    public async Task<ServiceResult<PagedResult<DatasetRecordListItemDto>>> GetPagedAsync(
        long organizationId,
        long datasetId,
        long requestingUserId,
        int page,
        int pageSize,
        string? sortBy,
        string? sortDir,
        RecordFilterRequest filter,
        CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<PagedResult<DatasetRecordListItemDto>>.Failure(accessError.Type, accessError.Message);
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var normalizedSortBy = sortBy?.ToLowerInvariant();
        if (normalizedSortBy is null || !SortableFields.Contains(normalizedSortBy))
        {
            normalizedSortBy = "createdat";
        }
        var descending = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);

        var filteredQuery = dbContext.DatasetRecords
            .Where(r => r.DatasetId == datasetId)
            .ApplyFilters(filter, dbContext);

        var totalCount = await filteredQuery.CountAsync(cancellationToken);

        var sortedQuery = normalizedSortBy switch
        {
            "amount" => descending ? filteredQuery.OrderByDescending(r => r.Amount) : filteredQuery.OrderBy(r => r.Amount),
            "recorddate" => descending ? filteredQuery.OrderByDescending(r => r.RecordDate) : filteredQuery.OrderBy(r => r.RecordDate),
            "city" => descending ? filteredQuery.OrderByDescending(r => r.City) : filteredQuery.OrderBy(r => r.City),
            "externalid" => descending ? filteredQuery.OrderByDescending(r => r.ExternalId) : filteredQuery.OrderBy(r => r.ExternalId),
            _ => descending ? filteredQuery.OrderByDescending(r => r.CreatedAt) : filteredQuery.OrderBy(r => r.CreatedAt),
        };

        var items = await sortedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new DatasetRecordListItemDto(
                r.Id, r.ExternalId, r.AddressLine1, r.City, r.State, r.PostalCode, r.Category, r.Amount,
                r.RecordDate, r.Status, r.GeocodeStatus))
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResult<DatasetRecordListItemDto>>.Success(
            new PagedResult<DatasetRecordListItemDto>(items, totalCount, page, pageSize));
    }
}
