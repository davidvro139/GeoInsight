using GeoInsight.Application.Common;

namespace GeoInsight.Application.Audit;

public interface IAuditEventService
{
    Task<ServiceResult<PagedResult<AuditEventDto>>> GetPagedAsync(
        long organizationId, long requestingUserId, int page, int pageSize, CancellationToken cancellationToken);
}
