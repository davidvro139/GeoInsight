namespace GeoInsight.Domain.Entities;

public enum ImportStatus
{
    Uploaded,
    Validated,
    Committed,
    Failed,
}

public class Import
{
    public long Id { get; set; }

    public long DatasetId { get; set; }

    public required string OriginalFileName { get; set; }

    public required string StoredFileName { get; set; }

    public ImportStatus Status { get; set; } = ImportStatus.Uploaded;

    public int TotalRows { get; set; }

    public int ValidRows { get; set; }

    public int WarningRows { get; set; }

    public int InvalidRows { get; set; }

    public long UploadedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
