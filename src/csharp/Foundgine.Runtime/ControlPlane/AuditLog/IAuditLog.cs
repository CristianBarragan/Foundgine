namespace Foundgine.Runtime.ControlPlane.AuditLog;

public interface IAuditLog
{
    void Record(AuditEvent auditEvent);

    IReadOnlyList<AuditEvent> Query(string? toolName = null, string? tenant = null);
}

/// <summary>
/// Process-local, append-only audit log. Entries are never mutated or
/// removed once recorded. A deployment that needs durable or tamper-evident
/// audit history should back <see cref="IAuditLog"/> with external storage —
/// the append-only contract is what callers can rely on either way.
/// </summary>
public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly List<AuditEvent> _events = [];
    private readonly Lock _gate = new();

    public void Record(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        lock (_gate)
        {
            _events.Add(auditEvent);
        }
    }

    public IReadOnlyList<AuditEvent> Query(string? toolName = null, string? tenant = null)
    {
        lock (_gate)
        {
            IEnumerable<AuditEvent> query = _events;
            if (!string.IsNullOrWhiteSpace(toolName))
                query = query.Where(e => string.Equals(e.ToolName, toolName, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(tenant))
                query = query.Where(e => string.Equals(e.Tenant, tenant, StringComparison.Ordinal));
            return query.ToArray();
        }
    }
}