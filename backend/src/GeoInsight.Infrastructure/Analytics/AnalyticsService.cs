using GeoInsight.Application.Analytics;
using GeoInsight.Application.Common;
using GeoInsight.Application.Tracts;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using GeoInsight.Infrastructure.Records;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.Analytics;

/// <summary>
/// One endpoint bundles everything the dashboard needs, all computed from the
/// same filtered snapshot of the dataset's records (via the Phase 9 shared
/// RecordFilterQueryExtensions.ApplyFilters) — "recalculates based on
/// filters" holds structurally rather than needing six requests to agree.
/// </summary>
public class AnalyticsService(GeoInsightDbContext dbContext, IDatasetAccessControl accessControl) : IAnalyticsService
{
    private static readonly (decimal? Min, decimal? Max, string Label)[] IncomeBuckets =
    [
        (null, 40_000m, "<$40k"),
        (40_000m, 60_000m, "$40k–$60k"),
        (60_000m, 80_000m, "$60k–$80k"),
        (80_000m, 100_000m, "$80k–$100k"),
        (100_000m, null, "$100k+"),
    ];

    public async Task<ServiceResult<AnalyticsDto>> GetAnalyticsAsync(
        long organizationId, long datasetId, long requestingUserId, RecordFilterRequest filter, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<AnalyticsDto>.Failure(accessError.Type, accessError.Message);
        }

        var filteredRecords = await dbContext.DatasetRecords
            .Where(r => r.DatasetId == datasetId)
            .ApplyFilters(filter, dbContext)
            .ToListAsync(cancellationToken);

        var tracts = await GetInScopeTractsAsync(datasetId, cancellationToken);
        var geoids = tracts.Select(t => t.Geoid).ToList();
        var demographicsByGeoid = await dbContext.CensusDemographics
            .Where(d => geoids.Contains(d.CensusGeoid))
            .ToDictionaryAsync(d => d.CensusGeoid, cancellationToken);
        var incomeLevelsByGeoid = await dbContext.CensusTractIncomeLevels
            .Where(l => geoids.Contains(l.CensusGeoid))
            .ToDictionaryAsync(l => l.CensusGeoid, cancellationToken);
        var assessmentAreaCounties = await dbContext.AssessmentAreaCounties
            .Where(a => a.OrganizationId == organizationId)
            .Select(a => new { a.StateFips, a.CountyFips })
            .ToListAsync(cancellationToken);
        var assessmentArea = assessmentAreaCounties.Select(a => (a.StateFips, a.CountyFips)).ToHashSet();

        var summary = BuildSummary(filteredRecords, demographicsByGeoid, assessmentArea.Count > 0);
        var byCategory = filteredRecords
            .GroupBy(r => r.Category)
            .Select(g => new CategoryCountDto(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ToList();
        var byCounty = filteredRecords
            .Where(r => r.CensusCountyFips != null)
            .GroupBy(r => r.CensusCountyFips!)
            .Select(g => new CountyCountDto(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ToList();
        var byIncomeRange = BuildIncomeRangeBreakdown(filteredRecords, demographicsByGeoid);
        var byGeocodeStatus = filteredRecords
            .GroupBy(r => r.GeocodeStatus)
            .Select(g => new GeocodeStatusCountDto(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ToList();
        var overTime = filteredRecords
            .Where(r => r.RecordDate is not null)
            .GroupBy(r => $"{r.RecordDate!.Value.Year:D4}-{r.RecordDate.Value.Month:D2}")
            .Select(g => new TimeSeriesPointDto(g.Key, g.Count()))
            .OrderBy(p => p.Period)
            .ToList();
        var tractStatistics = BuildTractStatistics(tracts, demographicsByGeoid, incomeLevelsByGeoid, assessmentArea, filteredRecords);

        return ServiceResult<AnalyticsDto>.Success(new AnalyticsDto(
            summary, byCategory, byCounty, byIncomeRange, byGeocodeStatus, overTime, tractStatistics));
    }

    private static AnalyticsSummaryDto BuildSummary(
        List<DatasetRecord> records, Dictionary<string, CensusDemographic> demographicsByGeoid, bool hasAssessmentArea)
    {
        var recordCount = records.Count;
        var geocodedCount = records.Count(r => r.GeocodeStatus is "matched" or "manually_resolved");
        var failedCount = records.Count(r => r.GeocodeStatus == "failed");
        var totalAmount = records.Sum(r => r.Amount ?? 0);
        // Distinct tracts/counties the *filtered* records actually touch — not
        // the dataset's full imported-tract scope, so this genuinely reacts to
        // the active filter the way the acceptance criteria requires.
        var touchedGeoids = records.Where(r => r.CensusGeoid != null).Select(r => r.CensusGeoid!).Distinct().ToList();
        var tractCount = touchedGeoids.Count;
        var countyCount = records.Where(r => r.CensusCountyFips != null).Select(r => r.CensusCountyFips).Distinct().Count();
        var geocodedRate = recordCount > 0 ? Math.Round(geocodedCount / (decimal)recordCount * 100, 1) : 0m;
        // Only counts as "estimated" when a demographic row exists but is
        // synthetic — a touched tract with no row at all has nothing to
        // caveat (its figures just show as unknown, not fake).
        var demographicsAreEstimated = touchedGeoids.Any(g =>
            demographicsByGeoid.TryGetValue(g, out var d) && d.RawData is null);

        return new AnalyticsSummaryDto(
            recordCount, geocodedCount, geocodedRate, failedCount, totalAmount, tractCount, countyCount, demographicsAreEstimated,
            hasAssessmentArea);
    }

    private static List<IncomeRangeCountDto> BuildIncomeRangeBreakdown(
        List<DatasetRecord> records, Dictionary<string, CensusDemographic> demographicsByGeoid)
    {
        var counts = IncomeBuckets.ToDictionary(b => b.Label, _ => 0);
        var unknownCount = 0;

        foreach (var record in records)
        {
            decimal? income = record.CensusGeoid is not null && demographicsByGeoid.TryGetValue(record.CensusGeoid, out var d)
                ? d.MedianHouseholdIncome
                : null;

            if (income is null)
            {
                unknownCount++;
                continue;
            }

            var bucket = IncomeBuckets.First(b => (b.Min is null || income >= b.Min) && (b.Max is null || income < b.Max));
            counts[bucket.Label]++;
        }

        var result = IncomeBuckets.Select(b => new IncomeRangeCountDto(b.Label, counts[b.Label])).ToList();
        if (unknownCount > 0)
        {
            result.Add(new IncomeRangeCountDto("Unknown", unknownCount));
        }

        return result;
    }

    /// <summary>
    /// Only tracts the filtered records actually touch — not every tract in
    /// the dataset's in-scope counties (that list is what the Map tab's
    /// boundary/choropleth layer uses instead, where showing an entire
    /// county's outline is the point). A "geographic analysis" table listing
    /// hundreds of always-zero rows for tracts nothing happened in isn't
    /// analysis; this also keeps it consistent with Summary.TractCount, which
    /// already only counts tracts the filtered records touch.
    /// </summary>
    private static List<TractStatisticDto> BuildTractStatistics(
        List<CensusTract> tracts, Dictionary<string, CensusDemographic> demographicsByGeoid,
        Dictionary<string, CensusTractIncomeLevel> incomeLevelsByGeoid,
        HashSet<(string StateFips, string CountyFips)> assessmentArea, List<DatasetRecord> records)
    {
        var recordsByGeoid = records
            .Where(r => r.CensusGeoid is not null)
            .GroupBy(r => r.CensusGeoid!)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Total: g.Sum(r => r.Amount ?? 0)));

        var tractsByGeoid = tracts.ToDictionary(t => t.Geoid);

        var rows = recordsByGeoid.Select(kvp =>
        {
            var (geoid, stats) = (kvp.Key, kvp.Value);
            var tract = tractsByGeoid.GetValueOrDefault(geoid);
            var population = demographicsByGeoid.TryGetValue(geoid, out var d) ? d.Population : null;
            var incomeTier = incomeLevelsByGeoid.TryGetValue(geoid, out var l) && l.IncomeTier != TractIncomeTier.Unknown
                ? l.IncomeTier
                : (TractIncomeTier?)null;
            // null = this org has no Assessment Area configured at all, distinct
            // from "configured, but this tract falls outside it" (false).
            var inAssessmentArea = assessmentArea.Count == 0 || tract is null
                ? (bool?)null
                : assessmentArea.Contains((tract.StateFips, tract.CountyFips));
            decimal? perThousand = population is > 0 ? Math.Round(stats.Count / (population.Value / 1000m), 2) : null;
            var averageAmount = stats.Count > 0 ? Math.Round(stats.Total / stats.Count, 2) : 0m;

            return (Geoid: geoid, TractName: tract?.TractName, CountyFips: tract?.CountyFips ?? "", Population: population,
                RecordCount: stats.Count, RecordsPer1000: perThousand, TotalAmount: stats.Total, AverageAmount: averageAmount,
                IncomeTier: incomeTier, InAssessmentArea: inAssessmentArea);
        }).ToList();

        // Phase 14: compare each tract's coverage (records/1,000 population)
        // against its county and the whole filtered region — the standard
        // normalized indicator, not raw counts/dollars which aren't
        // comparable across tracts of different population sizes.
        decimal? Average(IEnumerable<decimal?> values)
        {
            var present = values.Where(v => v is not null).Select(v => v!.Value).ToList();
            return present.Count > 0 ? present.Average() : null;
        }

        var regionAverage = Average(rows.Select(r => r.RecordsPer1000));
        var countyAverages = rows.GroupBy(r => r.CountyFips).ToDictionary(g => g.Key, g => Average(g.Select(r => r.RecordsPer1000)));

        return rows.Select(r =>
        {
            var countyAverage = countyAverages[r.CountyFips];
            decimal? deviation = r.RecordsPer1000 is { } value && countyAverage is > 0
                ? Math.Round((value - countyAverage.Value) / countyAverage.Value * 100, 1)
                : null;

            return new TractStatisticDto(
                r.Geoid, r.TractName, r.CountyFips, r.Population, r.RecordCount, r.RecordsPer1000, r.TotalAmount, r.AverageAmount,
                countyAverage, regionAverage, deviation, r.IncomeTier, r.InAssessmentArea);
        })
        .OrderByDescending(t => t.RecordCount)
        .ToList();
    }

    private async Task<List<CensusTract>> GetInScopeTractsAsync(long datasetId, CancellationToken cancellationToken)
    {
        var counties = await dbContext.DatasetRecords
            .Where(r => r.DatasetId == datasetId && r.CensusStateFips != null && r.CensusCountyFips != null)
            .Select(r => new CountyRef(r.CensusStateFips!, r.CensusCountyFips!))
            .Distinct()
            .ToListAsync(cancellationToken);

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
}
