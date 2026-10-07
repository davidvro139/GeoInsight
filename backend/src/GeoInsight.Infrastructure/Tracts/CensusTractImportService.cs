using GeoInsight.Application.Common;
using GeoInsight.Application.Tracts;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using GeoInsight.Infrastructure.Records;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.Tracts;

public class CensusTractImportService(
    GeoInsightDbContext dbContext, IDatasetAccessControl accessControl, IOrganizationAccessControl organizationAccessControl,
    IBackgroundJobClient backgroundJobClient)
    : ICensusTractImportService
{
    private static readonly HashSet<ChoroplethMetric> DemographicMetrics =
    [
        ChoroplethMetric.Population, ChoroplethMetric.MedianHouseholdIncome,
        ChoroplethMetric.PovertyRate, ChoroplethMetric.MinorityPopulationPercent,
    ];

    public async Task<ServiceResult<TractImportStatusDto>> GetStatusAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<TractImportStatusDto>.Failure(accessError.Type, accessError.Message);
        }

        var (needed, imported) = await ComputeCountyStatusAsync(datasetId, cancellationToken);
        var hasUnassignedCoordinates = await HasUnassignedCoordinatesAsync(datasetId, cancellationToken);
        var hasEstimatedDemographics = await HasEstimatedDemographicsAsync(datasetId, cancellationToken);
        return ServiceResult<TractImportStatusDto>.Success(
            new TractImportStatusDto(
                needed, imported, AllImported: needed.Count == 0 && !hasUnassignedCoordinates, hasUnassignedCoordinates,
                hasEstimatedDemographics));
    }

    private async Task<bool> HasEstimatedDemographicsAsync(long datasetId, CancellationToken cancellationToken)
    {
        var tracts = await GetInScopeTractsAsync(datasetId, cancellationToken);
        if (tracts.Count == 0)
        {
            return false;
        }

        var geoids = tracts.Select(t => t.Geoid).ToList();
        var realGeoids = await dbContext.CensusDemographics
            .Where(d => geoids.Contains(d.CensusGeoid) && d.RawData != null)
            .Select(d => d.CensusGeoid)
            .ToHashSetAsync(cancellationToken);

        return geoids.Any(g => !realGeoids.Contains(g));
    }

    public async Task<ServiceResult<bool>> RequestDemographicsRefreshAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Analyst, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<bool>.Failure(accessError.Type, accessError.Message);
        }

        var hasKey = await dbContext.Organizations
            .Where(o => o.Id == organizationId)
            .Select(o => o.CensusApiKeyEncrypted != null)
            .SingleOrDefaultAsync(cancellationToken);
        if (!hasKey)
        {
            return ServiceResult<bool>.Failure(
                ServiceErrorType.Validation, "Configure a Census API key in Organization Settings first.");
        }

        var tracts = await GetInScopeTractsAsync(datasetId, cancellationToken);
        var geoids = tracts.Select(t => t.Geoid).ToList();
        if (geoids.Count > 0)
        {
            backgroundJobClient.Enqueue<ITractImportRunner>(
                r => r.RefreshDemographicsAsync(datasetId, geoids, JobCancellationToken.Null));
        }

        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<bool>> RequestImportAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Analyst, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<bool>.Failure(accessError.Type, accessError.Message);
        }

        var (needed, _) = await ComputeCountyStatusAsync(datasetId, cancellationToken);
        foreach (var stateGroup in needed.GroupBy(c => c.StateFips))
        {
            var countyFipsList = stateGroup.Select(c => c.CountyFips).ToList();
            backgroundJobClient.Enqueue<ITractImportRunner>(
                r => r.RunAsync(datasetId, stateGroup.Key, countyFipsList, JobCancellationToken.Null));
        }

        // No new counties to download, but some records (e.g. CSV-supplied
        // coordinates) may still have no CensusGeoid — those never had a
        // geocoder-derived county to appear in `needed` in the first place, so
        // this is the only path that ever gives them one.
        if (needed.Count == 0 && await HasUnassignedCoordinatesAsync(datasetId, cancellationToken))
        {
            backgroundJobClient.Enqueue<ITractImportRunner>(r => r.BackfillOnlyAsync(datasetId, JobCancellationToken.Null));
        }

        return ServiceResult<bool>.Success(true);
    }

    private Task<bool> HasUnassignedCoordinatesAsync(long datasetId, CancellationToken cancellationToken) =>
        dbContext.DatasetRecords.AnyAsync(
            r => r.DatasetId == datasetId && r.Latitude != null && r.Longitude != null && r.CensusGeoid == null,
            cancellationToken);

    public async Task<ServiceResult<IReadOnlyList<TractBoundaryDto>>> GetBoundariesAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<IReadOnlyList<TractBoundaryDto>>.Failure(accessError.Type, accessError.Message);
        }

        var tracts = await GetInScopeTractsAsync(datasetId, cancellationToken);
        var geoids = tracts.Select(t => t.Geoid).ToList();
        var demographicsByGeoid = await dbContext.CensusDemographics
            .Where(d => geoids.Contains(d.CensusGeoid))
            .ToDictionaryAsync(d => d.CensusGeoid, cancellationToken);
        var incomeLevelsByGeoid = await dbContext.CensusTractIncomeLevels
            .Where(l => geoids.Contains(l.CensusGeoid))
            .ToDictionaryAsync(l => l.CensusGeoid, cancellationToken);

        var boundaries = tracts.Select(t => new TractBoundaryDto(
            t.Geoid, t.TractName, t.CountyFips, t.Boundary,
            demographicsByGeoid.TryGetValue(t.Geoid, out var d)
                ? new TractDemographicsDto(
                    d.Population, d.MedianHouseholdIncome, d.PovertyRate, d.MedianAge,
                    d.OwnerOccupiedPercent, d.RenterOccupiedPercent, d.MinorityPopulationPercent, d.SurveyYear,
                    IsEstimated: d.RawData is null)
                : null,
            ToIncomeLevelDto(incomeLevelsByGeoid, t.Geoid))).ToList();

        return ServiceResult<IReadOnlyList<TractBoundaryDto>>.Success(boundaries);
    }

    public async Task<ServiceResult<IReadOnlyList<TractMetricDto>>> GetChoroplethAsync(
        long organizationId, long datasetId, long requestingUserId, ChoroplethMetric metric, RecordFilterRequest filter,
        CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<IReadOnlyList<TractMetricDto>>.Failure(accessError.Type, accessError.Message);
        }

        var tracts = await GetInScopeTractsAsync(datasetId, cancellationToken);
        var geoids = tracts.Select(t => t.Geoid).ToList();

        if (metric == ChoroplethMetric.IncomeLevel)
        {
            var incomeLevelsByGeoid = await dbContext.CensusTractIncomeLevels
                .Where(l => geoids.Contains(l.CensusGeoid))
                .ToDictionaryAsync(l => l.CensusGeoid, cancellationToken);

            var tierValues = geoids.Select(g => new TractMetricDto(g,
                incomeLevelsByGeoid.TryGetValue(g, out var l) && l.IncomeTier != TractIncomeTier.Unknown
                    ? (decimal)l.IncomeTier
                    : null)).ToList();

            return ServiceResult<IReadOnlyList<TractMetricDto>>.Success(tierValues);
        }

        if (DemographicMetrics.Contains(metric))
        {
            var demographicsByGeoid = await dbContext.CensusDemographics
                .Where(d => geoids.Contains(d.CensusGeoid))
                .ToDictionaryAsync(d => d.CensusGeoid, cancellationToken);

            var values = geoids.Select(g => new TractMetricDto(g,
                demographicsByGeoid.TryGetValue(g, out var d) ? metric switch
                {
                    ChoroplethMetric.Population => (decimal?)d.Population,
                    ChoroplethMetric.MedianHouseholdIncome => d.MedianHouseholdIncome,
                    ChoroplethMetric.PovertyRate => d.PovertyRate,
                    ChoroplethMetric.MinorityPopulationPercent => d.MinorityPopulationPercent,
                    _ => null,
                } : null)).ToList();

            return ServiceResult<IReadOnlyList<TractMetricDto>>.Success(values);
        }

        // Record-derived metrics: aggregate the FILTERED records, grouped by tract.
        var recordStats = await dbContext.DatasetRecords
            .Where(r => r.DatasetId == datasetId && r.CensusGeoid != null && geoids.Contains(r.CensusGeoid!))
            .ApplyFilters(filter, dbContext)
            .GroupBy(r => r.CensusGeoid!)
            .Select(g => new { Geoid = g.Key, Count = g.Count(), Total = g.Sum(r => r.Amount ?? 0) })
            .ToDictionaryAsync(x => x.Geoid, cancellationToken);

        if (metric == ChoroplethMetric.RecordsPer1000)
        {
            var populationByGeoid = await dbContext.CensusDemographics
                .Where(d => geoids.Contains(d.CensusGeoid))
                .ToDictionaryAsync(d => d.CensusGeoid, d => d.Population, cancellationToken);

            var perThousand = geoids.Select(g =>
            {
                if (!recordStats.TryGetValue(g, out var stats))
                {
                    return new TractMetricDto(g, 0m);
                }
                if (!populationByGeoid.TryGetValue(g, out var population) || population is null or 0)
                {
                    return new TractMetricDto(g, null);
                }
                return new TractMetricDto(g, Math.Round(stats.Count / (population.Value / 1000m), 2));
            }).ToList();

            return ServiceResult<IReadOnlyList<TractMetricDto>>.Success(perThousand);
        }

        var results = geoids.Select(g =>
        {
            if (!recordStats.TryGetValue(g, out var stats))
            {
                return new TractMetricDto(g, 0m);
            }
            return new TractMetricDto(g, metric switch
            {
                ChoroplethMetric.RecordCount => (decimal?)stats.Count,
                ChoroplethMetric.TotalAmount => stats.Total,
                ChoroplethMetric.AverageAmount => stats.Count > 0 ? Math.Round(stats.Total / stats.Count, 2) : 0m,
                _ => null,
            });
        }).ToList();

        return ServiceResult<IReadOnlyList<TractMetricDto>>.Success(results);
    }

    public async Task<ServiceResult<bool>> RequestCountyImportAsync(
        long organizationId, long requestingUserId, string stateFips, string countyFips, CancellationToken cancellationToken)
    {
        var accessError = await organizationAccessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Administrator, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<bool>.Failure(accessError.Type, accessError.Message);
        }

        backgroundJobClient.Enqueue<ITractImportRunner>(
            r => r.ImportStandaloneCountyAsync(organizationId, stateFips, countyFips, JobCancellationToken.Null));

        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<IReadOnlyList<ImportedCountyDto>>> GetImportedCountiesAsync(
        long organizationId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await organizationAccessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<IReadOnlyList<ImportedCountyDto>>.Failure(accessError.Type, accessError.Message);
        }

        // GroupBy-into-a-record isn't translatable by every provider (fails on
        // EF Core InMemory, used by this app's own test suite) — project the
        // two plain columns first, then group/count in memory. census_tracts
        // is at most a few thousand rows at this app's scale, same reasoning
        // already used for the point-in-polygon backfill.
        var stateCountyPairs = await dbContext.CensusTracts
            .Select(t => new { t.StateFips, t.CountyFips })
            .ToListAsync(cancellationToken);

        var counties = stateCountyPairs
            .GroupBy(t => (t.StateFips, t.CountyFips))
            .Select(g => new ImportedCountyDto(g.Key.StateFips, g.Key.CountyFips, g.Count()))
            .OrderBy(c => c.StateFips).ThenBy(c => c.CountyFips)
            .ToList();

        return ServiceResult<IReadOnlyList<ImportedCountyDto>>.Success(counties);
    }

    /// <summary>
    /// County-scoped counterpart to GetBoundariesAsync (which is dataset-
    /// scoped) — lets an already-imported county's real tract boundaries +
    /// demographics be viewed with no dataset involved at all.
    /// </summary>
    public async Task<ServiceResult<IReadOnlyList<TractBoundaryDto>>> GetCountyBoundariesAsync(
        long organizationId, long requestingUserId, string stateFips, string countyFips, CancellationToken cancellationToken)
    {
        var accessError = await organizationAccessControl.RequireRoleAsync(
            organizationId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<IReadOnlyList<TractBoundaryDto>>.Failure(accessError.Type, accessError.Message);
        }

        var tracts = await dbContext.CensusTracts
            .Where(t => t.StateFips == stateFips && t.CountyFips == countyFips)
            .ToListAsync(cancellationToken);

        var geoids = tracts.Select(t => t.Geoid).ToList();
        var demographicsByGeoid = await dbContext.CensusDemographics
            .Where(d => geoids.Contains(d.CensusGeoid))
            .ToDictionaryAsync(d => d.CensusGeoid, cancellationToken);
        var incomeLevelsByGeoid = await dbContext.CensusTractIncomeLevels
            .Where(l => geoids.Contains(l.CensusGeoid))
            .ToDictionaryAsync(l => l.CensusGeoid, cancellationToken);

        var boundaries = tracts.Select(t => new TractBoundaryDto(
            t.Geoid, t.TractName, t.CountyFips, t.Boundary,
            demographicsByGeoid.TryGetValue(t.Geoid, out var d)
                ? new TractDemographicsDto(
                    d.Population, d.MedianHouseholdIncome, d.PovertyRate, d.MedianAge,
                    d.OwnerOccupiedPercent, d.RenterOccupiedPercent, d.MinorityPopulationPercent, d.SurveyYear,
                    IsEstimated: d.RawData is null)
                : null,
            ToIncomeLevelDto(incomeLevelsByGeoid, t.Geoid))).ToList();

        return ServiceResult<IReadOnlyList<TractBoundaryDto>>.Success(boundaries);
    }

    private static TractIncomeLevelDto? ToIncomeLevelDto(
        Dictionary<string, CensusTractIncomeLevel> incomeLevelsByGeoid, string geoid) =>
        incomeLevelsByGeoid.TryGetValue(geoid, out var l)
            ? new TractIncomeLevelDto(l.IncomeTier, l.TractMedianFamilyIncome, l.AreaMedianFamilyIncome, l.IncomePercentageOfAreaMedian, l.MsaMdName)
            : null;

    private async Task<List<CensusTract>> GetInScopeTractsAsync(long datasetId, CancellationToken cancellationToken)
    {
        var counties = await DatasetCountiesAsync(datasetId, cancellationToken);

        var tracts = new List<CensusTract>();
        foreach (var stateGroup in counties.GroupBy(c => c.StateFips))
        {
            var countyFipsList = stateGroup.Select(c => c.CountyFips).ToList();
            var stateTracts = await dbContext.CensusTracts
                .Where(t => t.StateFips == stateGroup.Key && countyFipsList.Contains(t.CountyFips))
                .ToListAsync(cancellationToken);
            tracts.AddRange(stateTracts);
        }

        return tracts;
    }

    private async Task<List<CountyRef>> DatasetCountiesAsync(long datasetId, CancellationToken cancellationToken) =>
        await dbContext.DatasetRecords
            .Where(r => r.DatasetId == datasetId && r.CensusStateFips != null && r.CensusCountyFips != null)
            .Select(r => new CountyRef(r.CensusStateFips!, r.CensusCountyFips!))
            .Distinct()
            .ToListAsync(cancellationToken);

    private async Task<(List<CountyRef> Needed, List<CountyRef> Imported)> ComputeCountyStatusAsync(
        long datasetId, CancellationToken cancellationToken)
    {
        var counties = await DatasetCountiesAsync(datasetId, cancellationToken);

        var needed = new List<CountyRef>();
        var imported = new List<CountyRef>();
        foreach (var county in counties)
        {
            var hasTracts = await dbContext.CensusTracts.AnyAsync(
                t => t.StateFips == county.StateFips && t.CountyFips == county.CountyFips, cancellationToken);
            (hasTracts ? imported : needed).Add(county);
        }

        return (needed, imported);
    }
}
