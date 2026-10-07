using GeoInsight.Application.Common;

namespace GeoInsight.Application.Organizations;

public interface IOrganizationService
{
    Task<ServiceResult<OrganizationDetailDto>> CreateAsync(
        long creatorUserId, CreateOrganizationRequest request, CancellationToken cancellationToken);

    Task<ServiceResult<IReadOnlyList<OrganizationSummaryDto>>> GetMineAsync(
        long userId, CancellationToken cancellationToken);

    Task<ServiceResult<OrganizationDetailDto>> GetByIdAsync(
        long organizationId, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<IReadOnlyList<OrganizationMemberDto>>> GetMembersAsync(
        long organizationId, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<OrganizationMemberDto>> AddMemberAsync(
        long organizationId, long requestingUserId, AddMemberRequest request, CancellationToken cancellationToken);

    Task<ServiceResult<OrganizationMemberDto>> ChangeMemberRoleAsync(
        long organizationId,
        long requestingUserId,
        long targetUserId,
        ChangeMemberRoleRequest request,
        CancellationToken cancellationToken);

    Task<ServiceResult<bool>> RemoveMemberAsync(
        long organizationId, long requestingUserId, long targetUserId, CancellationToken cancellationToken);

    Task<ServiceResult<bool>> SetCensusApiKeyAsync(
        long organizationId, long requestingUserId, string? apiKey, CancellationToken cancellationToken);
}
