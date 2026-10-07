using GeoInsight.Api.Common;
using GeoInsight.Application.Common;
using GeoInsight.Application.Tracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:long}/datasets/{datasetId:long}/tracts")]
public class CensusTractsController(ICensusTractImportService tractImportService) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<TractImportStatusDto>> GetStatus(
        long organizationId, long datasetId, CancellationToken cancellationToken)
    {
        var result = await tractImportService.GetStatusAsync(organizationId, datasetId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("import")]
    public async Task<ActionResult<bool>> RequestImport(
        long organizationId, long datasetId, CancellationToken cancellationToken)
    {
        var result = await tractImportService.RequestImportAsync(organizationId, datasetId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("refresh-demographics")]
    public async Task<ActionResult<bool>> RequestDemographicsRefresh(
        long organizationId, long datasetId, CancellationToken cancellationToken)
    {
        var result = await tractImportService.RequestDemographicsRefreshAsync(organizationId, datasetId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TractBoundaryDto>>> GetBoundaries(
        long organizationId, long datasetId, CancellationToken cancellationToken)
    {
        var result = await tractImportService.GetBoundariesAsync(organizationId, datasetId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("choropleth")]
    public async Task<ActionResult<IReadOnlyList<TractMetricDto>>> GetChoropleth(
        long organizationId, long datasetId, [FromQuery] ChoroplethMetric metric, [FromQuery] RecordFilterRequest? filter,
        CancellationToken cancellationToken)
    {
        var result = await tractImportService.GetChoroplethAsync(
            organizationId, datasetId, this.GetUserId(), metric, filter ?? new RecordFilterRequest(), cancellationToken);
        return result.ToActionResult(this);
    }
}
