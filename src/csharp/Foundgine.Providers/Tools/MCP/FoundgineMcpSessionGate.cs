namespace Foundgine.Providers.Tools.MCP;

public enum FoundgineMcpSessionGateAction { Proceed, Reject }

/// <param name="Action">Gate action.</param>
/// <param name="HttpStatus">Http status.</param>
/// <param name="IssuedSessionId">Set only when a new session was created and must be returned to the client.</param>
public sealed record FoundgineMcpSessionGateOutcome(
    FoundgineMcpSessionGateAction Action, int HttpStatus, string? IssuedSessionId)
{
    public bool Proceeds => Action == FoundgineMcpSessionGateAction.Proceed;
}

/// <summary>
/// Transport-neutral HTTP policy for MCP sessions: decides, per request, whether to proceed, which status to answer with,
/// and which session id to issue. The transport owner parses the request, calls the gate, and acts on the outcome. The gate
/// never authenticates anything: <c>owner</c> must be the principal the host has already authenticated for this request, and
/// a <see cref="FoundgineMcpSessionGateAction.Proceed"/> outcome only means the session is acceptable, never that the
/// caller is authorized.
/// </summary>
/// <remarks>
/// <c>initialize</c> starts a new session and the response must carry the issued id in
/// <see cref="FoundgineMcpSessionManager.HeaderName"/>; any other POST needs a valid id (400 when missing, 404 when unknown,
/// expired, terminated, malformed or owned by someone else, deliberately indistinguishable); <c>DELETE</c> terminates
/// (204, or 404 for anything that was not a live session of this owner). A full session table answers 503. With an audit
/// sink the gate reports issued, rejected and terminated sessions and a full table; the reason for a rejection goes to the
/// audit trail only, never to the client.
/// </remarks>
public sealed class FoundgineMcpSessionGate
{
    private readonly FoundgineMcpSessionManager _sessions;
    private readonly IFoundgineMcpAuditSink? _audit;
    private readonly Func<long> _clock;

    public FoundgineMcpSessionGate(
        FoundgineMcpSessionManager sessions, IFoundgineMcpAuditSink? audit = null, Func<long>? unixMillisecondsClock = null)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _audit = audit;
        _clock = unixMillisecondsClock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private void Emit(FoundgineMcpAuditEventType type, string? subject, string? owner, string? detail) =>
        FoundgineMcpAudit.Emit(_audit, new FoundgineMcpAuditEvent(type, _clock(), subject, owner, detail));

    /// <param name="jsonRpcMethod">The JSON-RPC <c>method</c> of the body, or null if it could not be read.</param>
    /// <param name="sessionHeader">The raw <c>Mcp-Session-Id</c> header, or null.</param>
    /// <param name="owner">The authenticated principal of this request, or null when sessions are not owner-bound.</param>
    public FoundgineMcpSessionGateOutcome AdmitPost(string? jsonRpcMethod, string? sessionHeader, string? owner = null)
    {
        if (jsonRpcMethod == "initialize")
        {
            try
            {
                var id = _sessions.Create(owner).SessionId;
                Emit(FoundgineMcpAuditEventType.SessionIssued, FoundgineMcpSessionManager.Fingerprint(id), owner, null);
                return new(FoundgineMcpSessionGateAction.Proceed, 200, id);
            }
            catch (InvalidOperationException)
            {
                Emit(FoundgineMcpAuditEventType.SessionTableFull, null, owner, null);
                return new(FoundgineMcpSessionGateAction.Reject, 503, null);
            }
        }
        if (string.IsNullOrWhiteSpace(sessionHeader))
        {
            Emit(FoundgineMcpAuditEventType.SessionRejected, null, owner, "method=POST status=MISSING");
            return new(FoundgineMcpSessionGateAction.Reject, 400, null);
        }
        var validation = _sessions.Validate(sessionHeader, owner);
        if (validation.IsValid) return new(FoundgineMcpSessionGateAction.Proceed, 200, null);
        Emit(FoundgineMcpAuditEventType.SessionRejected, FoundgineMcpSessionManager.Fingerprint(sessionHeader), owner,
            $"method=POST status={ToAuditStatus(validation.Status)}");
        return new(FoundgineMcpSessionGateAction.Reject, 404, null);
    }

    /// <summary>
    /// Admits the GET that opens or resumes the server-to-client SSE stream: the session must exist and belong to this owner
    /// (400 when the header is missing, 404 for anything else). Resume semantics (Last-Event-ID) belong to
    /// <see cref="FoundgineMcpSseBuffer"/>, which the transport consults only after this proceeds.
    /// </summary>
    public FoundgineMcpSessionGateOutcome AdmitGet(string? sessionHeader, string? owner = null)
    {
        if (string.IsNullOrWhiteSpace(sessionHeader))
        {
            Emit(FoundgineMcpAuditEventType.SessionRejected, null, owner, "method=GET status=MISSING");
            return new(FoundgineMcpSessionGateAction.Reject, 400, null);
        }
        var validation = _sessions.Validate(sessionHeader, owner);
        if (validation.IsValid) return new(FoundgineMcpSessionGateAction.Proceed, 200, null);
        Emit(FoundgineMcpAuditEventType.SessionRejected, FoundgineMcpSessionManager.Fingerprint(sessionHeader), owner,
            $"method=GET status={ToAuditStatus(validation.Status)}");
        return new(FoundgineMcpSessionGateAction.Reject, 404, null);
    }

    /// <inheritdoc cref="FoundgineMcpSessionManager.IsLive"/>
    public bool IsLive(string? sessionId) => _sessions.IsLive(sessionId);

    /// <summary>Removes expired sessions from the store; schedule it periodically. Returns how many were removed.</summary>
    public int PurgeExpired() => _sessions.PurgeExpired();

    public FoundgineMcpSessionGateOutcome AdmitDelete(string? sessionHeader, string? owner = null)
    {
        if (string.IsNullOrWhiteSpace(sessionHeader))
        {
            Emit(FoundgineMcpAuditEventType.SessionRejected, null, owner, "method=DELETE status=MISSING");
            return new(FoundgineMcpSessionGateAction.Reject, 400, null);
        }
        var fingerprint = FoundgineMcpSessionManager.Fingerprint(sessionHeader);
        if (_sessions.Terminate(sessionHeader, owner))
        {
            Emit(FoundgineMcpAuditEventType.SessionTerminated, fingerprint, owner, null);
            return new(FoundgineMcpSessionGateAction.Proceed, 204, null);
        }
        Emit(FoundgineMcpAuditEventType.SessionRejected, fingerprint, owner, "method=DELETE status=NOT_LIVE");
        return new(FoundgineMcpSessionGateAction.Reject, 404, null);
    }

    // Same spelling as the Java runtime's enum names, so audit trails from both runtimes read alike.
    private static string ToAuditStatus(FoundgineMcpSessionStatus status) => status switch
    {
        FoundgineMcpSessionStatus.NotFound => "NOT_FOUND",
        FoundgineMcpSessionStatus.OwnerMismatch => "OWNER_MISMATCH",
        FoundgineMcpSessionStatus.Expired => "EXPIRED",
        _ => "VALID"
    };
}
