namespace Foundgine.Runtime.ControlPlane.AuditLog;

/// <summary>A durable append-only audit sink. Completion means the sink acknowledged persistence.</summary>
public interface IAuditEventStore
{
    ValueTask AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
