namespace GeoInsight.Domain.Entities;

public class Dataset
{
    public long Id { get; set; }

    public long OrganizationId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string DatasetType { get; set; } = "generic";

    public long CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsArchived { get; set; }

    public DateTime? ArchivedAt { get; set; }
}
