using GeoInsight.Api.Common;
using GeoInsight.Application.Geocoding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:long}/datasets/{datasetId:long}/geocode-jobs")]
public class GeocodeJobsController(IGeocodeJobService geocodeJobService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<GeocodeJobDto>> Create(long organizationId, long datasetId, CancellationToken cancellationToken)
    {
        var result = await geocodeJobService.CreateAsync(organizationId, datasetId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GeocodeJobDto>>> GetAll(
        long organizationId, long datasetId, CancellationToken cancellationToken)
    {
        var result = await geocodeJobService.GetAllAsync(organizationId, datasetId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("{jobId:long}")]
    public async Task<ActionResult<GeocodeJobDto>> GetById(
        long organizationId, long datasetId, long jobId, CancellationToken cancellationToken)
    {
        var result = await geocodeJobService.GetAsync(organizationId, datasetId, jobId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("{jobId:long}/cancel")]
    public async Task<ActionResult<bool>> Cancel(
        long organizationId, long datasetId, long jobId, CancellationToken cancellationToken)
    {
        var result = await geocodeJobService.CancelAsync(organizationId, datasetId, jobId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }
}
