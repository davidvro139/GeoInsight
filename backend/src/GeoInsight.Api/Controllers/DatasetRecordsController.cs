using GeoInsight.Api.Common;
using GeoInsight.Application.Common;
using GeoInsight.Application.Records;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:long}/datasets/{datasetId:long}/records")]
public class DatasetRecordsController(IDatasetRecordService recordService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<DatasetRecordListItemDto>>> GetAll(
        long organizationId,
        long datasetId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] RecordFilterRequest? filter = null,
        CancellationToken cancellationToken = default)
    {
        var result = await recordService.GetPagedAsync(
            organizationId, datasetId, this.GetUserId(), page, pageSize, sortBy, sortDir,
            filter ?? new RecordFilterRequest(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("points")]
    public async Task<ActionResult<IReadOnlyList<DatasetRecordPointDto>>> GetPoints(
        long organizationId, long datasetId, [FromQuery] RecordFilterRequest? filter, CancellationToken cancellationToken)
    {
        var result = await recordService.GetPointsAsync(
            organizationId, datasetId, this.GetUserId(), filter ?? new RecordFilterRequest(), cancellationToken);
        return result.ToActionResult(this);
    }
}
