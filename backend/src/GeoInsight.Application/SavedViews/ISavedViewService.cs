using GeoInsight.Application.Common;

namespace GeoInsight.Application.SavedViews;

public interface ISavedViewService
{
    Task<ServiceResult<IReadOnlyList<SavedViewDto>>> GetAllAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<SavedViewDto>> CreateAsync(
        long organizationId, long datasetId, long requestingUserId, CreateSavedViewRequest request, CancellationToken cancellationToken);

    Task<ServiceResult<SavedViewDto>> RenameAsync(
        long organizationId, long datasetId, long savedViewId, long requestingUserId, RenameSavedViewRequest request, CancellationToken cancellationToken);

    Task<ServiceResult<bool>> DeleteAsync(
        long organizationId, long datasetId, long savedViewId, long requestingUserId, CancellationToken cancellationToken);
}
