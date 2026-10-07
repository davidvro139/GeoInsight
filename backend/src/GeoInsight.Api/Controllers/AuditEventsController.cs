using GeoInsight.Api.Common;
using GeoInsight.Application.Audit;
using GeoInsight.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:long}/audit-events")]
public class AuditEventsController(IAuditEventService auditEventService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditEventDto>>> GetPaged(
        long organizationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var result = await auditEventService.GetPagedAsync(organizationId, this.GetUserId(), page, pageSize, cancellationToken);
        return result.ToActionResult(this);
    }
}
