namespace GeoInsight.Domain.Entities;

public class SavedView
{
    public long Id { get; set; }

    public long OrganizationId { get; set; }

    public long DatasetId { get; set; }

    public long UserId { get; set; }

    public required string Name { get; set; }

    /// <summary>JSON-serialized RecordFilterRequest.</summary>
    public string? Filters { get; set; }

    /// <summary>JSON-serialized MapConfigurationDto (showTracts, choroplethMetric).</summary>
    public string? MapConfiguration { get; set; }

    /// <summary>
    /// Reserved for a future configurable-columns feature — nothing in the app
    /// lets a user hide/reorder table columns yet, so this is always null.
    /// </summary>
    public string? VisibleColumns { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
