using Hangfire;

namespace GeoInsight.Infrastructure.Tracts;

/// <summary>
/// The method Hangfire actually invokes — lives in Infrastructure for the same
/// reason as IGeocodeJobRunner (Phase 5): IJobCancellationToken is a Hangfire
/// integration detail, not an Application-layer concern.
/// </summary>
public interface ITractImportRunner
{
    Task RunAsync(long datasetId, string stateFips, IReadOnlyList<string> countyFipsList, IJobCancellationToken cancellationToken);

    /// <summary>
    /// No new tracts to download — just retries point-in-polygon assignment for
    /// this dataset's own records that have coordinates but no CensusGeoid yet,
    /// against every tract already imported (not scoped to a state/county list,
    /// since a record with only raw lat/lon — e.g. from a CSV import — has no
    /// county to scope by until this runs). See CensusTractImportService for
    /// when this path is used instead of RunAsync.
    /// </summary>
    Task BackfillOnlyAsync(long datasetId, IJobCancellationToken cancellationToken);

    /// <summary>
    /// Re-fetches demographics for every given GEOID (not just ones missing a
    /// row) — used by the "Refresh Demographics" action to upgrade
    /// already-imported synthetic tracts once an organization configures a
    /// real Census API key.
    /// </summary>
    Task RefreshDemographicsAsync(long datasetId, IReadOnlyList<string> geoids, IJobCancellationToken cancellationToken);

    /// <summary>
    /// Imports real tract boundaries + demographics for one county with no
    /// dataset involved at all — the standalone "acquire Census data ahead of
    /// time" path triggered from Organization Settings.
    /// </summary>
    Task ImportStandaloneCountyAsync(long organizationId, string stateFips, string countyFips, IJobCancellationToken cancellationToken);
}
