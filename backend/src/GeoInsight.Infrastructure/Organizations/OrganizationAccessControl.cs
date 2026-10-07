using GeoInsight.Application.Common;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.Organizations;

public class OrganizationAccessControl(GeoInsightDbContext dbContext) : IOrganizationAccessControl
{
    public async Task<ServiceError?> RequireRoleAsync(
        long organizationId, long userId, OrganizationRole minimumRole, CancellationToken cancellationToken)
    {
        var organizationExists = await dbContext.Organizations.AnyAsync(o => o.Id == organizationId, cancellationToken);
        if (!organizationExists)
        {
            return new ServiceError(ServiceErrorType.NotFound, "Organization not found.");
        }

        var membership = await dbContext.OrganizationUsers.SingleOrDefaultAsync(
            ou => ou.OrganizationId == organizationId && ou.UserId == userId, cancellationToken);
        if (membership is null)
        {
            return new ServiceError(ServiceErrorType.Forbidden, "You are not a member of this organization.");
        }

        if (!membership.Role.MeetsMinimum(minimumRole))
        {
            return new ServiceError(ServiceErrorType.Forbidden, $"This action requires the {minimumRole} role or higher.");
        }

        return null;
    }
}
