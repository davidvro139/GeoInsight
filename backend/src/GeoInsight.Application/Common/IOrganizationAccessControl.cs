using GeoInsight.Domain.Entities;

namespace GeoInsight.Application.Common;

/// <summary>
/// Shared organization-membership/role check reused by every org-scoped
/// service (OrganizationService, DatasetService, and future ones) so the
/// "org missing -> NotFound, not a member -> Forbidden, role too low ->
/// Forbidden" logic lives in exactly one place.
/// </summary>
public interface IOrganizationAccessControl
{
    Task<ServiceError?> RequireRoleAsync(
        long organizationId, long userId, OrganizationRole minimumRole, CancellationToken cancellationToken);
}
