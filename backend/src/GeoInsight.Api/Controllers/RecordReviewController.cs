using GeoInsight.Api.Common;
using GeoInsight.Application.Common;
using GeoInsight.Application.Review;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:long}/datasets/{datasetId:long}/records/review")]
public class RecordReviewController(IRecordReviewService reviewService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ReviewRecordDto>>> GetQueue(
        long organizationId, long datasetId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await reviewService.GetQueueAsync(organizationId, datasetId, this.GetUserId(), page, pageSize, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("{recordId:long}")]
    public async Task<ActionResult<ReviewRecordDto>> GetById(
        long organizationId, long datasetId, long recordId, CancellationToken cancellationToken)
    {
        var result = await reviewService.GetAsync(organizationId, datasetId, recordId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("{recordId:long}/accept-candidate")]
    public async Task<ActionResult<ReviewRecordDto>> AcceptCandidate(
        long organizationId, long datasetId, long recordId, [FromBody] AcceptCandidateRequest request, CancellationToken cancellationToken)
    {
        var result = await reviewService.AcceptCandidateAsync(organizationId, datasetId, recordId, request, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("{recordId:long}/correct-address")]
    public async Task<ActionResult<ReviewRecordDto>> CorrectAddress(
        long organizationId, long datasetId, long recordId, [FromBody] CorrectAddressRequest request, CancellationToken cancellationToken)
    {
        var result = await reviewService.CorrectAddressAsync(organizationId, datasetId, recordId, request, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("{recordId:long}/coordinates")]
    public async Task<ActionResult<ReviewRecordDto>> SetCoordinates(
        long organizationId, long datasetId, long recordId, [FromBody] SetCoordinatesRequest request, CancellationToken cancellationToken)
    {
        var result = await reviewService.SetCoordinatesAsync(organizationId, datasetId, recordId, request, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("{recordId:long}/dismiss")]
    public async Task<ActionResult<ReviewRecordDto>> Dismiss(
        long organizationId, long datasetId, long recordId, [FromBody] DismissRequest request, CancellationToken cancellationToken)
    {
        var result = await reviewService.DismissAsync(organizationId, datasetId, recordId, request, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }
}
