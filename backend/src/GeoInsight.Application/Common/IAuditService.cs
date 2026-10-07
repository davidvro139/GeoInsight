namespace GeoInsight.Application.Common;

/// <summary>
/// Minimal audit trail for Phase 6's "all manual corrections should be
/// audited" requirement — the full centralized audit system (more call sites,
/// an audit-history UI) is Phase 13. Log() only adds to the DbContext's change
/// tracker; the caller's own SaveChangesAsync commits the audited change and
/// its audit row together, atomically.
/// </summary>
public interface IAuditService
{
    void Log(long organizationId, long? userId, string action, string? entityType, string? entityId, object? metadata = null);
}
