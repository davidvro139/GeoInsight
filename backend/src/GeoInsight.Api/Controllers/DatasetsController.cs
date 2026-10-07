using GeoInsight.Api.Common;
using GeoInsight.Application.Datasets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:long}/datasets")]
public class DatasetsController(IDatasetService datasetService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DatasetSummaryDto>>> GetAll(
        long organizationId, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        var result = await datasetService.GetAllAsync(organizationId, this.GetUserId(), includeArchived, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost]
    public async Task<ActionResult<DatasetDetailDto>> Create(
        long organizationId, CreateDatasetRequest request, CancellationToken cancellationToken)
    {
        var result = await datasetService.CreateAsync(organizationId, this.GetUserId(), request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("{datasetId:long}")]
    public async Task<ActionResult<DatasetDetailDto>> GetById(
        long organizationId, long datasetId, CancellationToken cancellationToken)
    {
        var result = await datasetService.GetByIdAsync(organizationId, datasetId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPut("{datasetId:long}")]
    public async Task<ActionResult<DatasetDetailDto>> Update(
        long organizationId, long datasetId, UpdateDatasetRequest request, CancellationToken cancellationToken)
    {
        var result = await datasetService.UpdateAsync(organizationId, datasetId, this.GetUserId(), request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("{datasetId:long}/archive")]
    public async Task<ActionResult<bool>> Archive(long organizationId, long datasetId, CancellationToken cancellationToken)
    {
        var result = await datasetService.ArchiveAsync(organizationId, datasetId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("{datasetId:long}/unarchive")]
    public async Task<ActionResult<bool>> Unarchive(long organizationId, long datasetId, CancellationToken cancellationToken)
    {
        var result = await datasetService.UnarchiveAsync(organizationId, datasetId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }
}
