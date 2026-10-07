using GeoInsight.Application.Common;
using GeoInsight.Application.HmdaBenchmark;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using GeoInsight.Infrastructure.Records;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.HmdaBenchmark;

public class HmdaBenchmarkService(
    GeoInsightDbContext dbContext, IDatasetAccessControl accessControl, IHmdaBenchmarkSource hmdaBenchmarkSource)
    : IHmdaBenchmarkService
{
    public async Task<ServiceResult<IReadOnlyList<HmdaCountyBenchmarkDto>>> GetBenchmarkAsync(
        long organizationId, long datasetId, long requestingUserId, int year, RecordFilterRequest filter,
        CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<IReadOnlyList<HmdaCountyBenchmarkDto>>.Failure(accessError.Type, accessError.Message);
        }

        var filteredRecords = await dbContext.DatasetRecords
            .Where(r => r.DatasetId == datasetId)
            .ApplyFilters(filter, dbContext)
            .ToListAsync(cancellationToken);

        // Records with no assigned county have nothing to benchmark against —
        // same "excluded, not folded into a fake bucket" treatment
        // AnalyticsService.BuildSummary already gives ungeocoded records.
        var byCounty = filteredRecords
            .Where(r => r.CensusStateFips is not null && r.CensusCountyFips is not null)
            .GroupBy(r => (StateFips: r.CensusStateFips!, CountyFips: r.CensusCountyFips!))
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Total: g.Sum(r => r.Amount ?? 0)));

        var rows = new List<HmdaCountyBenchmarkDto>();
        foreach (var (county, stats) in byCounty)
        {
            var fullCountyFips = county.StateFips + county.CountyFips;
            var market = await hmdaBenchmarkSource.GetOriginatedLoanAggregateAsync(fullCountyFips, year, cancellationToken);
            var sharePercent = market is { Count: > 0 }
                ? Math.Round(stats.Count / (decimal)market.Count * 100, 2)
                : (decimal?)null;

            rows.Add(new HmdaCountyBenchmarkDto(
                county.StateFips, county.CountyFips, stats.Count, stats.Total, market?.Count, market?.TotalAmount, sharePercent));
        }

        return ServiceResult<IReadOnlyList<HmdaCountyBenchmarkDto>>.Success(
            rows.OrderByDescending(r => r.DatasetRecordCount).ToList());
    }
}
