using GeoInsight.Application.Common;

namespace GeoInsight.Application.Review;

public interface IRecordReviewService
{
    Task<ServiceResult<PagedResult<ReviewRecordDto>>> GetQueueAsync(
        long organizationId, long datasetId, long requestingUserId, int page, int pageSize, CancellationToken cancellationToken);

    Task<ServiceResult<ReviewRecordDto>> GetAsync(
        long organizationId, long datasetId, long recordId, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<ReviewRecordDto>> AcceptCandidateAsync(
        long organizationId, long datasetId, long recordId, AcceptCandidateRequest request, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<ReviewRecordDto>> CorrectAddressAsync(
        long organizationId, long datasetId, long recordId, CorrectAddressRequest request, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<ReviewRecordDto>> SetCoordinatesAsync(
        long organizationId, long datasetId, long recordId, SetCoordinatesRequest request, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<ReviewRecordDto>> DismissAsync(
        long organizationId, long datasetId, long recordId, DismissRequest request, long requestingUserId, CancellationToken cancellationToken);
}
