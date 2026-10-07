using GeoInsight.Api.Common;
using GeoInsight.Application.Exports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:long}/datasets/{datasetId:long}/exports")]
public class ExportsController(IExportService exportService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ExportDto>> Create(
        long organizationId, long datasetId, CreateExportRequest request, CancellationToken cancellationToken)
    {
        var result = await exportService.CreateAsync(organizationId, datasetId, this.GetUserId(), request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExportDto>>> GetAll(
        long organizationId, long datasetId, CancellationToken cancellationToken)
    {
        var result = await exportService.GetAllAsync(organizationId, datasetId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("{exportId:long}/download")]
    public async Task<IActionResult> Download(
        long organizationId, long datasetId, long exportId, CancellationToken cancellationToken)
    {
        var result = await exportService.DownloadAsync(organizationId, datasetId, exportId, this.GetUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return result.ToActionResult(this).Result!;
        }

        var file = result.Value!;
        return File(file.Content, file.ContentType, file.FileName);
    }
}
