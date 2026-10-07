using System.Text.Json;
using GeoInsight.Application.Common;
using GeoInsight.Application.SavedViews;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.SavedViews;

/// <summary>
/// Saved views are personal bookmarks of the Map page's filter/tract/choropleth
/// state, not dataset-mutating actions — so unlike import/geocode/tract-import
/// (Analyst+), any org member (Viewer+) can create and open their own. Renaming
/// or deleting someone *else's* view needs Administrator, for cleanup after a
/// user leaves the org.
/// </summary>
public class SavedViewService(
    GeoInsightDbContext dbContext, IDatasetAccessControl datasetAccessControl, IOrganizationAccessControl organizationAccessControl,
    IAuditService auditService)
    : ISavedViewService
{
    public async Task<ServiceResult<IReadOnlyList<SavedViewDto>>> GetAllAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await datasetAccessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<IReadOnlyList<SavedViewDto>>.Failure(accessError.Type, accessError.Message);
        }

        var rows = await (
            from v in dbContext.SavedViews
            join u in dbContext.Users on v.UserId equals u.Id
            where v.DatasetId == datasetId && v.OrganizationId == organizationId
            orderby v.UpdatedAt descending
            select new { View = v, OwnerDisplayName = u.DisplayName }
        ).ToListAsync(cancellationToken);

        var dtos = rows.Select(r => ToDto(r.View, r.OwnerDisplayName)).ToList();
        return ServiceResult<IReadOnlyList<SavedViewDto>>.Success(dtos);
    }

    public async Task<ServiceResult<SavedViewDto>> CreateAsync(
        long organizationId, long datasetId, long requestingUserId, CreateSavedViewRequest request, CancellationToken cancellationToken)
    {
        var accessError = await datasetAccessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<SavedViewDto>.Failure(accessError.Type, accessError.Message);
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return ServiceResult<SavedViewDto>.Failure(ServiceErrorType.Validation, "Name is required.");
        }
        if (name.Length > 200)
        {
            return ServiceResult<SavedViewDto>.Failure(ServiceErrorType.Validation, "Name must be 200 characters or fewer.");
        }

        var now = DateTime.UtcNow;
        var view = new SavedView
        {
            OrganizationId = organizationId,
            DatasetId = datasetId,
            UserId = requestingUserId,
            Name = name,
            Filters = request.Filters is null ? null : JsonSerializer.Serialize(request.Filters),
            MapConfiguration = request.MapConfiguration is null ? null : JsonSerializer.Serialize(request.MapConfiguration),
            CreatedAt = now,
            UpdatedAt = now,
        };
        dbContext.SavedViews.Add(view);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Logged after the insert (not batched into it) — the auto-increment
        // Id doesn't exist until SaveChangesAsync assigns it.
        auditService.Log(organizationId, requestingUserId, "saved_view.create", "saved_view", view.Id.ToString(),
            new { datasetId, name = view.Name });
        await dbContext.SaveChangesAsync(cancellationToken);

        var ownerDisplayName = await dbContext.Users
            .Where(u => u.Id == requestingUserId)
            .Select(u => u.DisplayName)
            .SingleAsync(cancellationToken);

        return ServiceResult<SavedViewDto>.Success(ToDto(view, ownerDisplayName));
    }

    public async Task<ServiceResult<SavedViewDto>> RenameAsync(
        long organizationId, long datasetId, long savedViewId, long requestingUserId, RenameSavedViewRequest request, CancellationToken cancellationToken)
    {
        var loadResult = await LoadForMutationAsync(organizationId, datasetId, savedViewId, requestingUserId, cancellationToken);
        if (loadResult.Error is not null)
        {
            return ServiceResult<SavedViewDto>.Failure(loadResult.Error.Type, loadResult.Error.Message);
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return ServiceResult<SavedViewDto>.Failure(ServiceErrorType.Validation, "Name is required.");
        }
        if (name.Length > 200)
        {
            return ServiceResult<SavedViewDto>.Failure(ServiceErrorType.Validation, "Name must be 200 characters or fewer.");
        }

        var view = loadResult.View!;
        view.Name = name;
        view.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var ownerDisplayName = await dbContext.Users
            .Where(u => u.Id == view.UserId)
            .Select(u => u.DisplayName)
            .SingleAsync(cancellationToken);

        return ServiceResult<SavedViewDto>.Success(ToDto(view, ownerDisplayName));
    }

    public async Task<ServiceResult<bool>> DeleteAsync(
        long organizationId, long datasetId, long savedViewId, long requestingUserId, CancellationToken cancellationToken)
    {
        var loadResult = await LoadForMutationAsync(organizationId, datasetId, savedViewId, requestingUserId, cancellationToken);
        if (loadResult.Error is not null)
        {
            return ServiceResult<bool>.Failure(loadResult.Error.Type, loadResult.Error.Message);
        }

        dbContext.SavedViews.Remove(loadResult.View!);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<bool>.Success(true);
    }

    /// <summary>
    /// Shared by RenameAsync/DeleteAsync: base Viewer+ dataset access, then the
    /// view must exist in this dataset/org, then the requester must own it or
    /// be an Administrator.
    /// </summary>
    private async Task<(SavedView? View, ServiceError? Error)> LoadForMutationAsync(
        long organizationId, long datasetId, long savedViewId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await datasetAccessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return (null, accessError);
        }

        var view = await dbContext.SavedViews.SingleOrDefaultAsync(
            v => v.Id == savedViewId && v.DatasetId == datasetId && v.OrganizationId == organizationId, cancellationToken);
        if (view is null)
        {
            return (null, new ServiceError(ServiceErrorType.NotFound, "Saved view not found."));
        }

        if (view.UserId != requestingUserId)
        {
            var adminError = await organizationAccessControl.RequireRoleAsync(
                organizationId, requestingUserId, OrganizationRole.Administrator, cancellationToken);
            if (adminError is not null)
            {
                return (null, new ServiceError(ServiceErrorType.Forbidden, "Only the owner or an Administrator can do that."));
            }
        }

        return (view, null);
    }

    private static SavedViewDto ToDto(SavedView view, string ownerDisplayName) => new(
        view.Id,
        view.Name,
        view.UserId,
        ownerDisplayName,
        view.CreatedAt,
        view.UpdatedAt,
        view.Filters is null ? null : JsonSerializer.Deserialize<RecordFilterRequest>(view.Filters),
        view.MapConfiguration is null ? null : JsonSerializer.Deserialize<MapConfigurationDto>(view.MapConfiguration));
}
