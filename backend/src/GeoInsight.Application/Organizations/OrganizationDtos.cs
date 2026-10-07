using GeoInsight.Domain.Entities;

namespace GeoInsight.Application.Organizations;

public record CreateOrganizationRequest(string Name);

public record OrganizationSummaryDto(long Id, string Name, OrganizationRole MyRole, int MemberCount);

public record OrganizationDetailDto(long Id, string Name, DateTime CreatedAt, OrganizationRole MyRole, bool HasCensusApiKey);

public record SetCensusApiKeyRequest(string? ApiKey);

public record OrganizationMemberDto(
    long UserId,
    string Email,
    string DisplayName,
    OrganizationRole Role,
    DateTime JoinedAt);

public record AddMemberRequest(string Email, OrganizationRole Role);

public record ChangeMemberRoleRequest(OrganizationRole Role);
