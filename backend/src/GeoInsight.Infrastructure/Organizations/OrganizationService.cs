using GeoInsight.Application.Common;
using GeoInsight.Application.Organizations;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.Organizations;

public class OrganizationService(
    GeoInsightDbContext dbContext, UserManager<ApplicationUser> userManager, IOrganizationAccessControl accessControl,
    IAuditService auditService, IDataProtectionProvider dataProtection)
    : IOrganizationService
{
    private readonly IDataProtector censusApiKeyProtector = dataProtection.CreateProtector("CensusApiKey");

    public async Task<ServiceResult<OrganizationDetailDto>> CreateAsync(
        long creatorUserId, CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var organization = new Organization
        {
            Name = request.Name,
            CreatedAt = now,
            UpdatedAt = now,
        };
        organization.Members.Add(new OrganizationUser
        {
            UserId = creatorUserId,
            Role = OrganizationRole.Administrator,
            CreatedAt = now,
        });

        dbContext.Organizations.Add(organization);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<OrganizationDetailDto>.Success(
            new OrganizationDetailDto(organization.Id, organization.Name, organization.CreatedAt, OrganizationRole.Administrator, HasCensusApiKey: false));
    }

    public async Task<ServiceResult<IReadOnlyList<OrganizationSummaryDto>>> GetMineAsync(
        long userId, CancellationToken cancellationToken)
    {
        var summaries = await dbContext.OrganizationUsers
            .Where(ou => ou.UserId == userId)
            .Select(ou => new OrganizationSummaryDto(
                ou.Organization.Id,
                ou.Organization.Name,
                ou.Role,
                ou.Organization.Members.Count))
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<OrganizationSummaryDto>>.Success(summaries);
    }

    public async Task<ServiceResult<OrganizationDetailDto>> GetByIdAsync(
        long organizationId, long requestingUserId, CancellationToken cancellationToken)
    {
        var organization = await dbContext.Organizations.FindAsync([organizationId], cancellationToken);
        if (organization is null)
        {
            return ServiceResult<OrganizationDetailDto>.Failure(ServiceErrorType.NotFound, "Organization not found.");
        }

        var membership = await GetMembershipAsync(organizationId, requestingUserId, cancellationToken);
        if (membership is null)
        {
            return ServiceResult<OrganizationDetailDto>.Failure(
                ServiceErrorType.Forbidden, "You are not a member of this organization.");
        }

        return ServiceResult<OrganizationDetailDto>.Success(
            new OrganizationDetailDto(
                organization.Id, organization.Name, organization.CreatedAt, membership.Role,
                HasCensusApiKey: organization.CensusApiKeyEncrypted is not null));
    }

    public async Task<ServiceResult<IReadOnlyList<OrganizationMemberDto>>> GetMembersAsync(
        long organizationId, long requestingUserId, CancellationToken cancellationToken)
    {
        var access = await GetByIdAsync(organizationId, requestingUserId, cancellationToken);
        if (!access.Succeeded)
        {
            return ServiceResult<IReadOnlyList<OrganizationMemberDto>>.Failure(access.Error!.Type, access.Error.Message);
        }

        var members = await (
            from ou in dbContext.OrganizationUsers
            join u in dbContext.Users on ou.UserId equals u.Id
            where ou.OrganizationId == organizationId
            orderby ou.CreatedAt
            select new OrganizationMemberDto(u.Id, u.Email!, u.DisplayName, ou.Role, ou.CreatedAt)
        ).ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<OrganizationMemberDto>>.Success(members);
    }

    public async Task<ServiceResult<OrganizationMemberDto>> AddMemberAsync(
        long organizationId, long requestingUserId, AddMemberRequest request, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Administrator, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<OrganizationMemberDto>.Failure(accessError.Type, accessError.Message);
        }

        var targetUser = await userManager.FindByEmailAsync(request.Email);
        if (targetUser is null)
        {
            return ServiceResult<OrganizationMemberDto>.Failure(
                ServiceErrorType.Validation, "No account found with that email. Ask them to register first.");
        }

        var existingMembership = await GetMembershipAsync(organizationId, targetUser.Id, cancellationToken);
        if (existingMembership is not null)
        {
            return ServiceResult<OrganizationMemberDto>.Failure(
                ServiceErrorType.Conflict, "This user is already a member of the organization.");
        }

        var membership = new OrganizationUser
        {
            OrganizationId = organizationId,
            UserId = targetUser.Id,
            Role = request.Role,
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.OrganizationUsers.Add(membership);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<OrganizationMemberDto>.Success(
            new OrganizationMemberDto(targetUser.Id, targetUser.Email!, targetUser.DisplayName, membership.Role, membership.CreatedAt));
    }

    public async Task<ServiceResult<OrganizationMemberDto>> ChangeMemberRoleAsync(
        long organizationId,
        long requestingUserId,
        long targetUserId,
        ChangeMemberRoleRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Administrator, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<OrganizationMemberDto>.Failure(accessError.Type, accessError.Message);
        }

        var targetMembership = await GetMembershipAsync(organizationId, targetUserId, cancellationToken);
        if (targetMembership is null)
        {
            return ServiceResult<OrganizationMemberDto>.Failure(
                ServiceErrorType.NotFound, "That user is not a member of this organization.");
        }

        if (targetMembership.Role == OrganizationRole.Administrator && request.Role != OrganizationRole.Administrator)
        {
            var lastAdminError = await EnsureNotLastAdministratorAsync(organizationId, targetUserId, cancellationToken);
            if (lastAdminError is not null)
            {
                return ServiceResult<OrganizationMemberDto>.Failure(lastAdminError.Type, lastAdminError.Message);
            }
        }

        var previousRole = targetMembership.Role;
        targetMembership.Role = request.Role;
        auditService.Log(organizationId, requestingUserId, "organization.change_role", "organization_user", targetUserId.ToString(),
            new { previousRole = previousRole.ToString(), newRole = request.Role.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);

        var targetUser = await userManager.FindByIdAsync(targetUserId.ToString());
        return ServiceResult<OrganizationMemberDto>.Success(
            new OrganizationMemberDto(targetUserId, targetUser!.Email!, targetUser.DisplayName, targetMembership.Role, targetMembership.CreatedAt));
    }

    public async Task<ServiceResult<bool>> RemoveMemberAsync(
        long organizationId, long requestingUserId, long targetUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Administrator, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<bool>.Failure(accessError.Type, accessError.Message);
        }

        var targetMembership = await GetMembershipAsync(organizationId, targetUserId, cancellationToken);
        if (targetMembership is null)
        {
            return ServiceResult<bool>.Failure(ServiceErrorType.NotFound, "That user is not a member of this organization.");
        }

        if (targetMembership.Role == OrganizationRole.Administrator)
        {
            var lastAdminError = await EnsureNotLastAdministratorAsync(organizationId, targetUserId, cancellationToken);
            if (lastAdminError is not null)
            {
                return ServiceResult<bool>.Failure(lastAdminError.Type, lastAdminError.Message);
            }
        }

        dbContext.OrganizationUsers.Remove(targetMembership);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<bool>> SetCensusApiKeyAsync(
        long organizationId, long requestingUserId, string? apiKey, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Administrator, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<bool>.Failure(accessError.Type, accessError.Message);
        }

        var organization = await dbContext.Organizations.FindAsync([organizationId], cancellationToken);
        if (organization is null)
        {
            return ServiceResult<bool>.Failure(ServiceErrorType.NotFound, "Organization not found.");
        }

        organization.CensusApiKeyEncrypted = string.IsNullOrWhiteSpace(apiKey) ? null : censusApiKeyProtector.Protect(apiKey);
        organization.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Success(true);
    }

    private Task<OrganizationUser?> GetMembershipAsync(long organizationId, long userId, CancellationToken cancellationToken) =>
        dbContext.OrganizationUsers.SingleOrDefaultAsync(
            ou => ou.OrganizationId == organizationId && ou.UserId == userId, cancellationToken);

    private async Task<ServiceError?> EnsureNotLastAdministratorAsync(
        long organizationId, long targetUserId, CancellationToken cancellationToken)
    {
        var otherAdminExists = await dbContext.OrganizationUsers.AnyAsync(
            ou => ou.OrganizationId == organizationId && ou.UserId != targetUserId && ou.Role == OrganizationRole.Administrator,
            cancellationToken);

        return otherAdminExists
            ? null
            : new ServiceError(ServiceErrorType.Validation, "An organization must have at least one Administrator.");
    }
}
