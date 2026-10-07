namespace GeoInsight.Domain.Entities;

public class AuditEvent
{
    public long Id { get; set; }

    public long OrganizationId { get; set; }

    public long? UserId { get; set; }

    public required string Action { get; set; }

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    /// <summary>JSON-serialized free-form details about the event.</summary>
    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; }
}
