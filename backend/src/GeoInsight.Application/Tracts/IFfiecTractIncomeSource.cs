using GeoInsight.Domain.Entities;

namespace GeoInsight.Application.Tracts;

public record FfiecTractIncomeRecord(
    string Geoid,
    TractIncomeTier IncomeTier,
    decimal? TractMedianFamilyIncome,
    decimal? AreaMedianFamilyIncome,
    decimal? IncomePercentageOfAreaMedian,
    string? MsaMdName);

/// <summary>
/// Looks up FFIEC's official, precomputed tract income classification
/// (Low/Moderate/Middle/Upper, relative to the tract's MSA/MD or statewide
/// non-metro median family income) — real government reference data for every
/// U.S. census tract, bundled with this app rather than fetched live (FFIEC
/// republishes it yearly as a flat file, not an API). See
/// FfiecTractIncomeSource for the survey period this bundle covers.
/// </summary>
public interface IFfiecTractIncomeSource
{
    bool TryGetValue(string geoid, out FfiecTractIncomeRecord record);
}
