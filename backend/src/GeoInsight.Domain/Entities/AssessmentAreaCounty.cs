namespace GeoInsight.Domain.Entities;

/// <summary>
/// One county in an organization's self-declared CRA Assessment Area — a
/// pure organizational fact, independent of whether that county's Census
/// data has been imported yet (see CensusTract/CensusDemographic, which
/// track data availability, not a bank's declared service area).
/// </summary>
public class AssessmentAreaCounty
{
    public long Id { get; set; }

    public long OrganizationId { get; set; }

    public required string StateFips { get; set; }

    public required string CountyFips { get; set; }
}
