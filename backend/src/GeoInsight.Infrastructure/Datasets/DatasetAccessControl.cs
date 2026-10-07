using GeoInsight.Application.Common;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.Datasets;

public class DatasetAccessControl(GeoInsightDbContext dbContext, IOrganizationAccessControl organizationAccessControl)
    : IDatasetAccessControl
{
    public async Task<ServiceError?> RequireRoleAsync(
        long organizationId, long datasetId, long userId, OrganizationRole minimumRole, CancellationToken cancellationToken)
    {
        var accessError = await organizationAccessControl.RequireRoleAsync(organizationId, userId, minimumRole, cancellationToken);
        if (accessError is not null)
        {
            return accessError;
        }

        var datasetExists = await dbContext.Datasets.AnyAsync(
            d => d.Id == datasetId && d.OrganizationId == organizationId, cancellationToken);

        return datasetExists ? null : new ServiceError(ServiceErrorType.NotFound, "Dataset not found.");
    }
}
