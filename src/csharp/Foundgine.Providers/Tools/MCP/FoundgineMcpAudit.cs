namespace Foundgine.Providers.Tools.MCP;

public enum FoundgineMcpAuditEventType
{
    KeyActivated, KeyRotated, KeyAdded, KeyRetired, KeyRevoked,
    SessionIssued, SessionRejected, SessionTerminated, SessionTableFull,
    SseOpened, SseRejected, SseClosed
}

/// <summary>
/// A security-relevant MCP event for the host's audit trail. Events never carry secrets: no key material, no session id (a
/// short digest fingerprint identifies a session instead, see <see cref="FoundgineMcpSessionManager.Fingerprint"/>), no tokens.
/// </summary>
/// <param name="Type">Foundgine Type.</param>
/// <param name="AtUnixMilliseconds">Unix in milliseconds.</param>
/// <param name="Subject">The key id, or the session fingerprint; null when the subject was attacker-controlled garbage.</param>
/// <param name="Actor">The authenticated principal the request ran as, when known.</param>
/// <param name="Detail">Short machine-readable context such as <c>status=Expired</c> or <c>previous=k1</c>.</param>
public sealed record FoundgineMcpAuditEvent(
    FoundgineMcpAuditEventType Type, long AtUnixMilliseconds, string? Subject, string? Actor, string? Detail);

/// <summary>
/// Receives <see cref="FoundgineMcpAuditEvent"/>s. Called synchronously after the audited operation has already taken
/// effect, so keep it fast (hand off to a queue or logger). An exception thrown by a sink is swallowed: a broken audit
/// pipeline must never turn a revocation or a session check into a failure or undo it. Hosts that need guaranteed delivery
/// should make the sink itself durable and alert on its own failures.
/// </summary>
public interface IFoundgineMcpAuditSink
{
    void Record(FoundgineMcpAuditEvent auditEvent);
}

internal static class FoundgineMcpAudit
{
    internal static void Emit(IFoundgineMcpAuditSink? sink, FoundgineMcpAuditEvent auditEvent)
    {
        if (sink is null) return;
        try { sink.Record(auditEvent); }
        catch (Exception) { /* audit must not affect the operation */ }
    }
}
