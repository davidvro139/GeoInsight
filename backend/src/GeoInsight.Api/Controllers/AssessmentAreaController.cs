using GeoInsight.Api.Common;
using GeoInsight.Application.AssessmentArea;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

/// <summary>
/// An organization's self-declared CRA Assessment Area — a set of counties,
/// freely chosen independent of Census data import status (see
/// CensusDataController for that separate concern). Consumed by the
/// Analytics/Gap Analysis endpoint to flag tracts as inside/outside.
/// </summary>
[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:long}/assessment-area")]
public class AssessmentAreaController(IAssessmentAreaService assessmentAreaService) : ControllerBase
{
    [HttpGet("counties")]
    public async Task<ActionResult<IReadOnlyList<AssessmentAreaCountyDto>>> GetCounties(
        long organizationId, CancellationToken cancellationToken)
    {
        var result = await assessmentAreaService.GetCountiesAsync(organizationId, this.GetUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("counties")]
    public async Task<ActionResult<bool>> AddCounty(
        long organizationId, AssessmentAreaCountyDto request, CancellationToken cancellationToken)
    {
        var result = await assessmentAreaService.AddCountyAsync(
            organizationId, this.GetUserId(), request.StateFips, request.CountyFips, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpDelete("counties/{stateFips}/{countyFips}")]
    public async Task<ActionResult<bool>> RemoveCounty(
        long organizationId, string stateFips, string countyFips, CancellationToken cancellationToken)
    {
        var result = await assessmentAreaService.RemoveCountyAsync(
            organizationId, this.GetUserId(), stateFips, countyFips, cancellationToken);
        return result.ToActionResult(this);
    }
}
