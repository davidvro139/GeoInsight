namespace GeoInsight.Domain.Entities;

public class DatasetRecord
{
    public long Id { get; set; }

    public long DatasetId { get; set; }

    public long? ImportId { get; set; }

    public string? ExternalId { get; set; }

    public string? Category { get; set; }

    public decimal? Amount { get; set; }

    public DateOnly? RecordDate { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// A business status carried in from the import (e.g. "active", "closed")
    /// — distinct from GeocodeStatus below, which the system manages. Not in
    /// 04_MYSQL_DATABASE_DESIGN.md's dataset_records table, but 02_PRODUCT_
    /// REQUIREMENTS.md §4 lists "status" as a standard optional import field.
    /// </summary>
    public string? Status { get; set; }

    public required string AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public required string City { get; set; }

    public required string State { get; set; }

    public string? PostalCode { get; set; }

    public string? NormalizedAddress { get; set; }

    public string? MatchedAddress { get; set; }

    public string GeocodeStatus { get; set; } = "pending";

    public string? MatchQuality { get; set; }

    public string? GeocoderSource { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public string? CensusStateFips { get; set; }

    public string? CensusCountyFips { get; set; }

    public string? CensusTract { get; set; }

    public string? CensusBlockGroup { get; set; }

    public string? CensusBlock { get; set; }

    public string? CensusGeoid { get; set; }

    public DateTime? GeocodedAt { get; set; }

    /// <summary>JSON-serialized list of GeocodeCandidate — populated only while
    /// GeocodeStatus is "ambiguous", so the Address Review Queue can list them
    /// and let a human pick one. Cleared the moment the record is resolved.</summary>
    public string? GeocodeCandidates { get; set; }

    /// <summary>Set when a human marks this record "intentionally unresolved"
    /// in the Address Review Queue. Not a geocode_status value (the PRD's status
    /// enum has no such state) — just removes the record from the active queue
    /// without pretending it has coordinates it doesn't.</summary>
    public DateTime? ReviewDismissedAt { get; set; }

    /// <summary>Raw JSON. Keyed by the CSV column header for any column not
    /// mapped to a standard field — see 02_PRODUCT_REQUIREMENTS.md §4 "define
    /// custom fields".</summary>
    public string? CustomData { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
