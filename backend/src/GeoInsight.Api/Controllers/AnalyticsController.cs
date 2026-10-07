using GeoInsight.Api.Common;
using GeoInsight.Application.Analytics;
using GeoInsight.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:long}/datasets/{datasetId:long}/analytics")]
public class AnalyticsController(IAnalyticsService analyticsService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AnalyticsDto>> GetAnalytics(
        long organizationId, long datasetId, [FromQuery] RecordFilterRequest? filter, CancellationToken cancellationToken)
    {
        var result = await analyticsService.GetAnalyticsAsync(
            organizationId, datasetId, this.GetUserId(), filter ?? new RecordFilterRequest(), cancellationToken);
        return result.ToActionResult(this);
    }
}
