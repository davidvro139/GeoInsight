using GeoInsight.Application.Tracts;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace GeoInsight.Infrastructure.Tracts;

/// <summary>
/// Imports tract boundaries for one state's requested counties (skipping any
/// GEOID already present — census_tracts is global reference data shared
/// across every dataset/org), then backfills tract assignment via
/// point-in-polygon for the triggering dataset's own records that have
/// coordinates but no CensusGeoid yet (Phase 6's manually-entered-coordinate
/// records). Never touches other datasets — only ever queries CensusTracts
/// (global) and DatasetRecords WHERE DatasetId == datasetId.
/// </summary>
public class TractImportRunner(
    GeoInsightDbContext dbContext,
    IStateTractSource tractSource,
    IDemographicDataSource placeholderDemographicSource,
    IAcsDemographicDataSource acsDemographicSource,
    IFfiecTractIncomeSource ffiecTractIncomeSource,
    IDataProtectionProvider dataProtection,
    ILogger<TractImportRunner> logger)
    : ITractImportRunner
{
    private const short PlaceholderSurveyYear = 2022;
    private const string FfiecSurveyPeriod = "2024-2026";
    private readonly IDataProtector censusApiKeyProtector = dataProtection.CreateProtector("CensusApiKey");

    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    public async Task RunAsync(long datasetId, string stateFips, IReadOnlyList<string> countyFipsList, IJobCancellationToken cancellationToken)
    {
        var organizationId = await dbContext.Datasets.Where(d => d.Id == datasetId).Select(d => d.OrganizationId).SingleAsync();
        await ImportCountyDataAsync(stateFips, countyFipsList, organizationId, forceRefreshDemographics: false, cancellationToken);

        await BackfillDatasetRecordsAsync(datasetId, stateFips, countyFipsList);
    }

    public async Task BackfillOnlyAsync(long datasetId, IJobCancellationToken cancellationToken)
    {
        await BackfillDatasetRecordsAsync(datasetId, stateFips: null, countyFipsList: null);
    }

    public async Task RefreshDemographicsAsync(long datasetId, IReadOnlyList<string> geoids, IJobCancellationToken cancellationToken)
    {
        var organizationId = await dbContext.Datasets.Where(d => d.Id == datasetId).Select(d => d.OrganizationId).SingleAsync();
        await UpsertDemographicsAsync(geoids, organizationId, forceRefresh: true);
    }

    /// <summary>
    /// The standalone, no-dataset entry point: an Administrator picks a
    /// state+county directly (Organization Settings), independent of any
    /// dataset ever touching it. Unlike RunAsync (only fetches demographics
    /// for tracts *missing* a row), this force-refreshes every tract in the
    /// county — the point of this entry point is "get me the best available
    /// data for this county now," combining what RunAsync and
    /// RefreshDemographicsAsync do separately for the dataset-triggered path.
    /// </summary>
    public async Task ImportStandaloneCountyAsync(long organizationId, string stateFips, string countyFips, IJobCancellationToken cancellationToken)
    {
        await ImportCountyDataAsync(stateFips, [countyFips], organizationId, forceRefreshDemographics: true, cancellationToken);
    }

    /// <summary>
    /// Downloads the state's tract shapefile, inserts any GEOIDs from the
    /// requested counties not already present (census_tracts is global
    /// reference data shared across every dataset/org), then upserts
    /// demographics for every tract in those counties — new ones always, and
    /// (if forceRefreshDemographics) already-present ones too.
    /// </summary>
    private async Task ImportCountyDataAsync(
        string stateFips, IReadOnlyList<string> countyFipsList, long organizationId, bool forceRefreshDemographics, IJobCancellationToken cancellationToken)
    {
        var existingGeoids = await dbContext.CensusTracts
            .Where(t => t.StateFips == stateFips)
            .Select(t => t.Geoid)
            .ToHashSetAsync();

        var features = await tractSource.GetTractsAsync(stateFips, CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();

        var countyFeatures = features.Where(f => countyFipsList.Contains(f.CountyFips)).ToList();
        var newTracts = countyFeatures
            .Where(f => !existingGeoids.Contains(f.Geoid))
            .Select(f => new CensusTract
            {
                Geoid = f.Geoid,
                StateFips = stateFips,
                CountyFips = f.CountyFips,
                TractCode = f.TractCode,
                TractName = f.TractName,
                Boundary = NormalizeToMultiPolygon(f.Boundary),
            })
            .ToList();

        if (newTracts.Count > 0)
        {
            dbContext.CensusTracts.AddRange(newTracts);
            await dbContext.SaveChangesAsync();
            logger.LogInformation("Imported {Count} new tracts for state {StateFips}", newTracts.Count, stateFips);
        }

        var targetGeoids = forceRefreshDemographics
            ? countyFeatures.Select(f => f.Geoid).ToList()
            : newTracts.Select(t => t.Geoid).ToList();
        if (targetGeoids.Count > 0)
        {
            await UpsertDemographicsAsync(targetGeoids, organizationId, forceRefreshDemographics);
            await UpsertIncomeLevelsAsync(targetGeoids);
        }
    }

    /// <summary>
    /// Unlike demographics, FFIEC income-level data is a deterministic bundled
    /// lookup (no live call, no synthetic fallback), so it always upserts
    /// every target GEOID rather than needing a forceRefresh flag — there's
    /// nothing to "refresh," the bundle is the bundle.
    /// </summary>
    private async Task UpsertIncomeLevelsAsync(IReadOnlyList<string> geoids)
    {
        var existing = await dbContext.CensusTractIncomeLevels
            .Where(l => geoids.Contains(l.CensusGeoid) && l.SurveyPeriod == FfiecSurveyPeriod)
            .ToDictionaryAsync(l => l.CensusGeoid);

        foreach (var geoid in geoids)
        {
            if (!ffiecTractIncomeSource.TryGetValue(geoid, out var record))
            {
                continue;
            }

            if (existing.TryGetValue(geoid, out var row))
            {
                row.IncomeTier = record.IncomeTier;
                row.TractMedianFamilyIncome = record.TractMedianFamilyIncome;
                row.AreaMedianFamilyIncome = record.AreaMedianFamilyIncome;
                row.IncomePercentageOfAreaMedian = record.IncomePercentageOfAreaMedian;
                row.MsaMdName = record.MsaMdName;
            }
            else
            {
                dbContext.CensusTractIncomeLevels.Add(new CensusTractIncomeLevel
                {
                    CensusGeoid = geoid,
                    SurveyPeriod = FfiecSurveyPeriod,
                    IncomeTier = record.IncomeTier,
                    TractMedianFamilyIncome = record.TractMedianFamilyIncome,
                    AreaMedianFamilyIncome = record.AreaMedianFamilyIncome,
                    IncomePercentageOfAreaMedian = record.IncomePercentageOfAreaMedian,
                    MsaMdName = record.MsaMdName,
                });
            }
        }

        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Fetches demographics for the given GEOIDs and upserts (not always-
    /// inserts) the (CensusGeoid, PlaceholderSurveyYear) row — every consumer
    /// of census_demographics (AnalyticsService, ExportService, this class'
    /// own choropleth/boundary queries) keys a dictionary purely by
    /// CensusGeoid, so a second row per GEOID at a different survey year would
    /// throw a duplicate-key exception at every one of those call sites. This
    /// also doubles as the "upgrade a synthetic tract to real" path once an
    /// organization adds a real API key — same upsert, just forceRefresh:true
    /// so already-present rows aren't skipped.
    /// </summary>
    private async Task UpsertDemographicsAsync(IReadOnlyList<string> geoids, long organizationId, bool forceRefresh)
    {
        var existing = await dbContext.CensusDemographics
            .Where(d => geoids.Contains(d.CensusGeoid) && d.SurveyYear == PlaceholderSurveyYear)
            .ToDictionaryAsync(d => d.CensusGeoid);

        var targets = forceRefresh ? geoids : geoids.Where(g => !existing.ContainsKey(g)).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var apiKey = await GetDecryptedApiKeyAsync(organizationId);
        var demographics = await FetchDemographicsAsync(targets, apiKey);

        foreach (var d in demographics)
        {
            if (existing.TryGetValue(d.Geoid, out var row))
            {
                row.Population = d.Population;
                row.MedianHouseholdIncome = d.MedianHouseholdIncome;
                row.PovertyRate = d.PovertyRate;
                row.MedianAge = d.MedianAge;
                row.OwnerOccupiedPercent = d.OwnerOccupiedPercent;
                row.RenterOccupiedPercent = d.RenterOccupiedPercent;
                row.MinorityPopulationPercent = d.MinorityPopulationPercent;
                row.RawData = d.RawJson;
            }
            else
            {
                dbContext.CensusDemographics.Add(new CensusDemographic
                {
                    CensusGeoid = d.Geoid,
                    SurveyYear = PlaceholderSurveyYear,
                    Population = d.Population,
                    MedianHouseholdIncome = d.MedianHouseholdIncome,
                    PovertyRate = d.PovertyRate,
                    MedianAge = d.MedianAge,
                    OwnerOccupiedPercent = d.OwnerOccupiedPercent,
                    RenterOccupiedPercent = d.RenterOccupiedPercent,
                    MinorityPopulationPercent = d.MinorityPopulationPercent,
                    RawData = d.RawJson,
                });
            }
        }

        await dbContext.SaveChangesAsync();
        // Infer the actual source from the results, not just whether a key was
        // configured — a configured key whose real fetch failed still falls
        // back to placeholder data (see FetchDemographicsAsync).
        var usedReal = demographics.Any(d => d.RawJson is not null);
        logger.LogInformation(
            "Upserted {Source} demographics for {Count} tracts", usedReal ? "real ACS" : "placeholder", demographics.Count);
    }

    private async Task<string?> GetDecryptedApiKeyAsync(long organizationId)
    {
        var encrypted = await dbContext.Organizations
            .Where(o => o.Id == organizationId)
            .Select(o => o.CensusApiKeyEncrypted)
            .SingleOrDefaultAsync();

        if (encrypted is null)
        {
            return null;
        }

        try
        {
            return censusApiKeyProtector.Unprotect(encrypted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to decrypt stored Census API key for organization {OrganizationId} — falling back to placeholder data", organizationId);
            return null;
        }
    }

    private async Task<IReadOnlyList<ParsedDemographics>> FetchDemographicsAsync(IReadOnlyList<string> geoids, string? apiKey)
    {
        if (apiKey is null)
        {
            return await placeholderDemographicSource.GetDemographicsAsync(geoids, apiKey: null, CancellationToken.None);
        }

        try
        {
            return await acsDemographicSource.GetDemographicsAsync(geoids, apiKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Infrastructure failure (bad key, network issue, rate limit) —
            // fall back rather than failing the whole tract-import job.
            logger.LogWarning(ex, "Real Census ACS demographics fetch failed for {Count} tracts — falling back to placeholder data", geoids.Count);
            return await placeholderDemographicSource.GetDemographicsAsync(geoids, apiKey: null, CancellationToken.None);
        }
    }

    private async Task BackfillDatasetRecordsAsync(long datasetId, string? stateFips, IReadOnlyList<string>? countyFipsList)
    {
        var unassigned = await dbContext.DatasetRecords
            .Where(r => r.DatasetId == datasetId && r.Latitude != null && r.Longitude != null && r.CensusGeoid == null)
            .ToListAsync();

        if (unassigned.Count == 0)
        {
            return;
        }

        // stateFips null means "no known scope" (e.g. CSV-supplied coordinates
        // with no geocoder-derived county) — match against every tract already
        // imported instead. This app only ever imports a handful of counties at
        // a time, so an unscoped query here is still cheap.
        var candidateTracts = stateFips is null
            ? await dbContext.CensusTracts.ToListAsync()
            : await dbContext.CensusTracts
                .Where(t => t.StateFips == stateFips && countyFipsList!.Contains(t.CountyFips))
                .ToListAsync();

        var assigned = 0;
        foreach (var record in unassigned)
        {
            var point = new Point((double)record.Longitude!.Value, (double)record.Latitude!.Value) { SRID = 4326 };
            var tract = candidateTracts.FirstOrDefault(t => t.Boundary.Contains(point));
            if (tract is null)
            {
                continue;
            }

            record.CensusStateFips = tract.StateFips;
            record.CensusCountyFips = tract.CountyFips;
            record.CensusTract = tract.TractCode;
            record.CensusGeoid = tract.Geoid;
            assigned++;
        }

        if (assigned > 0)
        {
            await dbContext.SaveChangesAsync();
            logger.LogInformation("Point-in-polygon assigned a tract to {Count} records in dataset {DatasetId}", assigned, datasetId);
        }
    }

    private static MultiPolygon NormalizeToMultiPolygon(Geometry geometry)
    {
        var polygons = geometry switch
        {
            Polygon polygon => [polygon],
            MultiPolygon multi => multi.Geometries.Cast<Polygon>().ToArray(),
            _ => throw new InvalidOperationException($"Unexpected tract geometry type: {geometry.GeometryType}"),
        };

        var multiPolygon = GeometryFactory.CreateMultiPolygon(polygons);
        multiPolygon.SRID = 4326;
        return multiPolygon;
    }
}
