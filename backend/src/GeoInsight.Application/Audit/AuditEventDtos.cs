namespace GeoInsight.Application.Audit;

/// <summary>Metadata is passed through as a raw JSON string — no per-action typed
/// shape; the UI renders it as-is rather than needing a type for every action.</summary>
public record AuditEventDto(
    long Id, string Action, string? EntityType, string? EntityId, string? Metadata, string? UserDisplayName, DateTime CreatedAt);
