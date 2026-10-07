using GeoInsight.Api.Common;
using GeoInsight.Application.SavedViews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:long}/datasets/{datasetId:long}/saved-views")]
public class SavedViewsController(ISavedViewService savedViewService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SavedViewDto>>> GetAll(
        long organizationId, long datasetId, CancellationToken cancellationToken)
    {
        var result = await savedViewService.GetAllAsync(organizationId, datasetId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost]
    public async Task<ActionResult<SavedViewDto>> Create(
        long organizationId, long datasetId, CreateSavedViewRequest request, CancellationToken cancellationToken)
    {
        var result = await savedViewService.CreateAsync(organizationId, datasetId, this.GetUserId(), request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPut("{savedViewId:long}")]
    public async Task<ActionResult<SavedViewDto>> Rename(
        long organizationId, long datasetId, long savedViewId, RenameSavedViewRequest request, CancellationToken cancellationToken)
    {
        var result = await savedViewService.RenameAsync(organizationId, datasetId, savedViewId, this.GetUserId(), request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpDelete("{savedViewId:long}")]
    public async Task<ActionResult<bool>> Delete(
        long organizationId, long datasetId, long savedViewId, CancellationToken cancellationToken)
    {
        var result = await savedViewService.DeleteAsync(organizationId, datasetId, savedViewId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }
}
