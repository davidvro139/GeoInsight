using GeoInsight.Application.Common;

namespace GeoInsight.Application.Tracts;

public interface ICensusTractImportService
{
    Task<ServiceResult<TractImportStatusDto>> GetStatusAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<bool>> RequestImportAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<bool>> RequestDemographicsRefreshAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<IReadOnlyList<TractBoundaryDto>>> GetBoundariesAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken);

    Task<ServiceResult<IReadOnlyList<TractMetricDto>>> GetChoroplethAsync(
        long organizationId, long datasetId, long requestingUserId, ChoroplethMetric metric, RecordFilterRequest filter,
        CancellationToken cancellationToken);

    /// <summary>
    /// Standalone, no-dataset Census data acquisition — an Administrator picks
    /// a state+county directly rather than this being derived from a
    /// dataset's own geocoded records.
    /// </summary>
    Task<ServiceResult<bool>> RequestCountyImportAsync(
        long organizationId, long requestingUserId, string stateFips, string countyFips, CancellationToken cancellationToken);

    /// <summary>
    /// Global — census_tracts is shared reference data, not org-scoped — but
    /// still requires organization membership to view, matching every other
    /// endpoint in this app.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<ImportedCountyDto>>> GetImportedCountiesAsync(
        long organizationId, long requestingUserId, CancellationToken cancellationToken);

    /// <summary>
    /// County-scoped counterpart to GetBoundariesAsync (dataset-scoped) — lets
    /// an already-imported county be viewed with no dataset involved.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<TractBoundaryDto>>> GetCountyBoundariesAsync(
        long organizationId, long requestingUserId, string stateFips, string countyFips, CancellationToken cancellationToken);
}
