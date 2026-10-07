namespace GeoInsight.Domain.Entities;

public enum ExportType
{
    Csv,
    Xlsx,
    GeoJson,
    Png,
    Pdf,
}

/// <summary>
/// Every export in this app finishes inside one HTTP request (no slow
/// external API involved, unlike geocoding/tract-boundary download), so a row
/// is only ever inserted after generation succeeds — Completed is the only
/// value this ever holds. The column still exists to match
/// 04_MYSQL_DATABASE_DESIGN.md's schema.
/// </summary>
public enum ExportStatus
{
    Completed,
}

public class Export
{
    public long Id { get; set; }

    public long OrganizationId { get; set; }

    public long DatasetId { get; set; }

    public long UserId { get; set; }

    public ExportType ExportType { get; set; }

    public ExportStatus Status { get; set; }

    /// <summary>The IFileStorageService-returned stored key, not the original file name.</summary>
    public string? FilePath { get; set; }

    /// <summary>JSON-serialized RecordFilterRequest — null for Png/Pdf, which have no server-side filter concept.</summary>
    public string? Filters { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
