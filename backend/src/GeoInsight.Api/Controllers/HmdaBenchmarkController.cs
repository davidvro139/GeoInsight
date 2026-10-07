using GeoInsight.Api.Common;
using GeoInsight.Application.Common;
using GeoInsight.Application.HmdaBenchmark;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

/// <summary>
/// On-demand comparison against the real, public CFPB HMDA mortgage-lending
/// market — kept separate from AnalyticsController since this is a live
/// external HTTP call per county, not a local DB computation.
/// </summary>
[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:long}/datasets/{datasetId:long}/hmda-benchmark")]
public class HmdaBenchmarkController(IHmdaBenchmarkService hmdaBenchmarkService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HmdaCountyBenchmarkDto>>> GetBenchmark(
        long organizationId, long datasetId, int year, [FromQuery] RecordFilterRequest? filter,
        CancellationToken cancellationToken)
    {
        var result = await hmdaBenchmarkService.GetBenchmarkAsync(
            organizationId, datasetId, this.GetUserId(), year, filter ?? new RecordFilterRequest(), cancellationToken);
        return result.ToActionResult(this);
    }
}
