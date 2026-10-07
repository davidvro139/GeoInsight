using GeoInsight.Domain.Entities;

namespace GeoInsight.Application.Common;

/// <summary>
/// Combines an organization role check with "this dataset actually belongs to
/// this organization" — the same pass/fail check ImportService needed
/// (org missing/not-a-member/role-too-low -> as IOrganizationAccessControl,
/// plus dataset missing or in a different org -> NotFound), shared so
/// DatasetRecordService doesn't duplicate it a third time.
/// </summary>
public interface IDatasetAccessControl
{
    Task<ServiceError?> RequireRoleAsync(
        long organizationId, long datasetId, long userId, OrganizationRole minimumRole, CancellationToken cancellationToken);
}
