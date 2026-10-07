using GeoInsight.Api.Common;
using GeoInsight.Application.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:long}/datasets/{datasetId:long}/imports")]
public class ImportsController(IImportService importService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ImportUploadResult>> Upload(
        long organizationId, long datasetId, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return Problem("No file was uploaded.", statusCode: StatusCodes.Status400BadRequest);
        }

        await using var stream = file.OpenReadStream();
        var result = await importService.UploadAsync(
            organizationId, datasetId, this.GetUserId(), stream, file.FileName, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ImportListItemDto>>> GetAll(
        long organizationId, long datasetId, CancellationToken cancellationToken)
    {
        var result = await importService.GetAllAsync(organizationId, datasetId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("{importId:long}/validate")]
    public async Task<ActionResult<ImportSummaryDto>> Validate(
        long organizationId, long datasetId, long importId, ImportValidateRequest request, CancellationToken cancellationToken)
    {
        var result = await importService.ValidateAsync(
            organizationId, datasetId, importId, this.GetUserId(), request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("{importId:long}/commit")]
    public async Task<ActionResult<ImportSummaryDto>> Commit(
        long organizationId, long datasetId, long importId, ImportValidateRequest request, CancellationToken cancellationToken)
    {
        var result = await importService.CommitAsync(
            organizationId, datasetId, importId, this.GetUserId(), request, cancellationToken);
        return result.ToActionResult(this);
    }
}
