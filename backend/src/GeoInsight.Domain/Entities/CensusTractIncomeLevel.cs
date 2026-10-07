namespace GeoInsight.Domain.Entities;

public enum TractIncomeTier
{
    Unknown,
    Low,
    Moderate,
    Middle,
    Upper,
}

/// <summary>
/// Global reference data keyed by (CensusGeoid, SurveyPeriod), matching
/// census_demographics' shared-reference-data model — a new FFIEC vintage adds
/// a row rather than overwriting the old one. Sourced from FFIEC's official
/// Census Tract List (not derived/estimated) — the real CRA-standard
/// classification of a tract's income relative to its MSA/MD (or statewide
/// non-metro) median family income.
/// </summary>
public class CensusTractIncomeLevel
{
    public long Id { get; set; }

    public required string CensusGeoid { get; set; }

    /// <summary>FFIEC vintage, e.g. "2024-2026" — a multi-year range, not a single year.</summary>
    public required string SurveyPeriod { get; set; }

    public TractIncomeTier IncomeTier { get; set; }

    public decimal? TractMedianFamilyIncome { get; set; }

    public decimal? AreaMedianFamilyIncome { get; set; }

    public decimal? IncomePercentageOfAreaMedian { get; set; }

    public string? MsaMdName { get; set; }
}
