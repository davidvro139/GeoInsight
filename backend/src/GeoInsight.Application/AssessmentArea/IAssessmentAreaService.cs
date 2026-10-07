using GeoInsight.Application.Common;

namespace GeoInsight.Application.AssessmentArea;

/// <summary>
/// An organization's self-declared CRA Assessment Area — the set of counties
/// it has committed to serving, freely chosen independent of whether that
/// county's Census data has been imported (see ICensusTractImportService for
/// that separate, data-availability concern). Consumed by AnalyticsService to
/// flag each Gap Analysis tract as inside/outside the area.
/// </summary>
public interface IAssessmentAreaService
{
    Task<ServiceResult<IReadOnlyList<AssessmentAreaCountyDto>>> GetCountiesAsync(
        long organizationId, long requestingUserId, CancellationToken cancellationToken);

    /// <summary>Idempotent — adding an already-declared county is a no-op success.</summary>
    Task<ServiceResult<bool>> AddCountyAsync(
        long organizationId, long requestingUserId, string stateFips, string countyFips, CancellationToken cancellationToken);

    /// <summary>Idempotent — removing a county that isn't declared is a no-op success.</summary>
    Task<ServiceResult<bool>> RemoveCountyAsync(
        long organizationId, long requestingUserId, string stateFips, string countyFips, CancellationToken cancellationToken);
}
