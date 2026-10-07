using GeoInsight.Application.Common;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;

namespace GeoInsight.Infrastructure.Records;

/// <summary>
/// Applies a RecordFilterRequest to a DatasetRecords query — the one place
/// every filterable field is translated to SQL, shared by the paged table,
/// the map points endpoint, and the tract choropleth's record-derived
/// metrics, so none of them can drift out of sync with each other.
/// </summary>
public static class RecordFilterQueryExtensions
{
    public static IQueryable<DatasetRecord> ApplyFilters(
        this IQueryable<DatasetRecord> query, RecordFilterRequest filter, GeoInsightDbContext dbContext)
    {
        if (filter.DateFrom is { } dateFrom)
        {
            query = query.Where(r => r.RecordDate != null && r.RecordDate >= dateFrom);
        }

        if (filter.DateTo is { } dateTo)
        {
            query = query.Where(r => r.RecordDate != null && r.RecordDate <= dateTo);
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            query = query.Where(r => r.Category != null && r.Category.Contains(filter.Category));
        }

        if (filter.MinAmount is { } minAmount)
        {
            query = query.Where(r => r.Amount != null && r.Amount >= minAmount);
        }

        if (filter.MaxAmount is { } maxAmount)
        {
            query = query.Where(r => r.Amount != null && r.Amount <= maxAmount);
        }

        if (!string.IsNullOrWhiteSpace(filter.City))
        {
            query = query.Where(r => r.City.Contains(filter.City));
        }

        if (!string.IsNullOrWhiteSpace(filter.State))
        {
            query = query.Where(r => r.State.Contains(filter.State));
        }

        if (!string.IsNullOrWhiteSpace(filter.CountyFips))
        {
            query = query.Where(r => r.CensusCountyFips == filter.CountyFips);
        }

        if (!string.IsNullOrWhiteSpace(filter.Tract))
        {
            query = query.Where(r => r.CensusTract == filter.Tract);
        }

        if (!string.IsNullOrWhiteSpace(filter.GeocodeStatus))
        {
            query = query.Where(r => r.GeocodeStatus == filter.GeocodeStatus);
        }

        if (filter.MinMedianIncome is { } minIncome)
        {
            query = query.Where(r => r.CensusGeoid != null && dbContext.CensusDemographics.Any(
                d => d.CensusGeoid == r.CensusGeoid && d.MedianHouseholdIncome != null && d.MedianHouseholdIncome >= minIncome));
        }

        if (filter.MaxMedianIncome is { } maxIncome)
        {
            query = query.Where(r => r.CensusGeoid != null && dbContext.CensusDemographics.Any(
                d => d.CensusGeoid == r.CensusGeoid && d.MedianHouseholdIncome != null && d.MedianHouseholdIncome <= maxIncome));
        }

        return query;
    }
}
