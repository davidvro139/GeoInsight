namespace GeoInsight.Domain.Entities;

/// <summary>
/// Global reference data keyed by (CensusGeoid, SurveyYear), matching
/// census_tracts' shared-reference-data model — a real ACS refresh for a new
/// vintage year adds a row rather than overwriting the old one.
/// </summary>
public class CensusDemographic
{
    public long Id { get; set; }

    public required string CensusGeoid { get; set; }

    public short SurveyYear { get; set; }

    public int? Population { get; set; }

    public decimal? MedianHouseholdIncome { get; set; }

    public decimal? PovertyRate { get; set; }

    public decimal? MedianAge { get; set; }

    public decimal? OwnerOccupiedPercent { get; set; }

    public decimal? RenterOccupiedPercent { get; set; }

    public decimal? MinorityPopulationPercent { get; set; }

    public string? RawData { get; set; }
}
