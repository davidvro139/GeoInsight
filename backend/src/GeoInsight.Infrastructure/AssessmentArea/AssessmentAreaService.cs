using GeoInsight.Application.AssessmentArea;
using GeoInsight.Application.Common;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.AssessmentArea;

public class AssessmentAreaService(GeoInsightDbContext dbContext, IOrganizationAccessControl organizationAccessControl)
    : IAssessmentAreaService
{
    public async Task<ServiceResult<IReadOnlyList<AssessmentAreaCountyDto>>> GetCountiesAsync(
        long organizationId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await organizationAccessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<IReadOnlyList<AssessmentAreaCountyDto>>.Failure(accessError.Type, accessError.Message);
        }

        var counties = await dbContext.AssessmentAreaCounties
            .Where(a => a.OrganizationId == organizationId)
            .OrderBy(a => a.StateFips).ThenBy(a => a.CountyFips)
            .Select(a => new AssessmentAreaCountyDto(a.StateFips, a.CountyFips))
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<AssessmentAreaCountyDto>>.Success(counties);
    }

    public async Task<ServiceResult<bool>> AddCountyAsync(
        long organizationId, long requestingUserId, string stateFips, string countyFips, CancellationToken cancellationToken)
    {
        var accessError = await organizationAccessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Administrator, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<bool>.Failure(accessError.Type, accessError.Message);
        }

        var exists = await dbContext.AssessmentAreaCounties.AnyAsync(
            a => a.OrganizationId == organizationId && a.StateFips == stateFips && a.CountyFips == countyFips,
            cancellationToken);
        if (!exists)
        {
            dbContext.AssessmentAreaCounties.Add(new AssessmentAreaCounty
            {
                OrganizationId = organizationId, StateFips = stateFips, CountyFips = countyFips,
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<bool>> RemoveCountyAsync(
        long organizationId, long requestingUserId, string stateFips, string countyFips, CancellationToken cancellationToken)
    {
        var accessError = await organizationAccessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Administrator, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<bool>.Failure(accessError.Type, accessError.Message);
        }

        var existing = await dbContext.AssessmentAreaCounties.SingleOrDefaultAsync(
            a => a.OrganizationId == organizationId && a.StateFips == stateFips && a.CountyFips == countyFips,
            cancellationToken);
        if (existing is not null)
        {
            dbContext.AssessmentAreaCounties.Remove(existing);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult<bool>.Success(true);
    }
}
