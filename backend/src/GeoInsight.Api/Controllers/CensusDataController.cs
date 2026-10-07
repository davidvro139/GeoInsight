using GeoInsight.Api.Common;
using GeoInsight.Application.Tracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

/// <summary>
/// Standalone Census data acquisition — decoupled from any dataset, unlike
/// CensusTractsController (which derives "needed counties" from a dataset's
/// own geocoded records). Lets an Administrator pre-load real tract
/// boundaries + demographics for a county before any dataset touches it.
/// </summary>
[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:long}/census-data")]
public class CensusDataController(ICensusTractImportService tractImportService) : ControllerBase
{
    [HttpPost("import")]
    public async Task<ActionResult<bool>> RequestImport(
        long organizationId, RequestCountyImportRequest request, CancellationToken cancellationToken)
    {
        var result = await tractImportService.RequestCountyImportAsync(
            organizationId, this.GetUserId(), request.StateFips, request.CountyFips, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("imported-counties")]
    public async Task<ActionResult<IReadOnlyList<ImportedCountyDto>>> GetImportedCounties(
        long organizationId, CancellationToken cancellationToken)
    {
        var result = await tractImportService.GetImportedCountiesAsync(organizationId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("counties/{stateFips}/{countyFips}/boundaries")]
    public async Task<ActionResult<IReadOnlyList<TractBoundaryDto>>> GetCountyBoundaries(
        long organizationId, string stateFips, string countyFips, CancellationToken cancellationToken)
    {
        var result = await tractImportService.GetCountyBoundariesAsync(
            organizationId, this.GetUserId(), stateFips, countyFips, cancellationToken);
        return result.ToActionResult(this);
    }
}
