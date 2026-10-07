using System.Text.Json;
using GeoInsight.Application.Common;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;

namespace GeoInsight.Infrastructure.Common;

public class AuditService(GeoInsightDbContext dbContext) : IAuditService
{
    public void Log(long organizationId, long? userId, string action, string? entityType, string? entityId, object? metadata = null)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId,
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Metadata = metadata is null ? null : JsonSerializer.Serialize(metadata),
            CreatedAt = DateTime.UtcNow,
        });
    }
}
