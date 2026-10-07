using GeoInsight.Application.Audit;
using GeoInsight.Application.Common;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.Audit;

/// <summary>
/// The read side of the audit trail — distinct from IAuditService (the
/// write-only logger every other service calls). Administrator-only per
/// 02_PRODUCT_REQUIREMENTS.md §1, where "view audit logs" appears only in
/// the Administrator capability list (Analyst/Viewer do not have it).
/// </summary>
public class AuditEventService(GeoInsightDbContext dbContext, IOrganizationAccessControl accessControl) : IAuditEventService
{
    public async Task<ServiceResult<PagedResult<AuditEventDto>>> GetPagedAsync(
        long organizationId, long requestingUserId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Administrator, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<PagedResult<AuditEventDto>>.Failure(accessError.Type, accessError.Message);
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = dbContext.AuditEvents.Where(a => a.OrganizationId == organizationId);
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await (
            from a in query
            join u in dbContext.Users on a.UserId equals u.Id into userJoin
            from u in userJoin.DefaultIfEmpty()
            orderby a.CreatedAt descending
            select new AuditEventDto(a.Id, a.Action, a.EntityType, a.EntityId, a.Metadata, u == null ? null : u.DisplayName, a.CreatedAt)
        )
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(cancellationToken);

        return ServiceResult<PagedResult<AuditEventDto>>.Success(new PagedResult<AuditEventDto>(items, totalCount, page, pageSize));
    }
}
