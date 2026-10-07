using GeoInsight.Api.Common;
using GeoInsight.Application.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations")]
public class OrganizationsController(IOrganizationService organizationService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<OrganizationDetailDto>> Create(
        CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        var result = await organizationService.CreateAsync(this.GetUserId(), request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrganizationSummaryDto>>> GetMine(CancellationToken cancellationToken)
    {
        var result = await organizationService.GetMineAsync(this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("{organizationId:long}")]
    public async Task<ActionResult<OrganizationDetailDto>> GetById(long organizationId, CancellationToken cancellationToken)
    {
        var result = await organizationService.GetByIdAsync(organizationId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("{organizationId:long}/members")]
    public async Task<ActionResult<IReadOnlyList<OrganizationMemberDto>>> GetMembers(
        long organizationId, CancellationToken cancellationToken)
    {
        var result = await organizationService.GetMembersAsync(organizationId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("{organizationId:long}/members")]
    public async Task<ActionResult<OrganizationMemberDto>> AddMember(
        long organizationId, AddMemberRequest request, CancellationToken cancellationToken)
    {
        var result = await organizationService.AddMemberAsync(organizationId, this.GetUserId(), request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPatch("{organizationId:long}/members/{userId:long}")]
    public async Task<ActionResult<OrganizationMemberDto>> ChangeMemberRole(
        long organizationId, long userId, ChangeMemberRoleRequest request, CancellationToken cancellationToken)
    {
        var result = await organizationService.ChangeMemberRoleAsync(
            organizationId, this.GetUserId(), userId, request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpDelete("{organizationId:long}/members/{userId:long}")]
    public async Task<ActionResult<bool>> RemoveMember(
        long organizationId, long userId, CancellationToken cancellationToken)
    {
        var result = await organizationService.RemoveMemberAsync(organizationId, this.GetUserId(), userId, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPut("{organizationId:long}/census-api-key")]
    public async Task<ActionResult<bool>> SetCensusApiKey(
        long organizationId, SetCensusApiKeyRequest request, CancellationToken cancellationToken)
    {
        var result = await organizationService.SetCensusApiKeyAsync(organizationId, this.GetUserId(), request.ApiKey, cancellationToken);
        return result.ToActionResult(this);
    }
}
