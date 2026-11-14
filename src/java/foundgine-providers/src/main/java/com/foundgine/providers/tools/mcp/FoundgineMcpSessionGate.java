package com.foundgine.providers.tools.mcp;

import java.util.Objects;
import java.util.function.LongSupplier;

/**
 * Transport-neutral HTTP policy for MCP sessions: decides, per request, whether to proceed, which status to answer
 * with, and which session id to issue. The transport owner (servlet filter, Spring controller) parses the request, calls the
 * gate, and acts on the {@link Outcome}. The gate never authenticates anything: {@code owner} must be the principal the
 * host has <em>already</em> authenticated for this request, and a {@link Action#PROCEED} outcome only means the session
 * is acceptable, never that the caller is authorized.
 *
 * <p>Rules: {@code initialize} starts a new session and the response must carry the issued id in
 * {@link FoundgineMcpSessionManager#HEADER_NAME}; any other POST needs a valid id (400 when missing, 404 when unknown,
 * expired, terminated, malformed or owned by someone else, deliberately indistinguishable); {@code DELETE} terminates
 * (204, or 404 for anything that was not a live session of this owner). A full session table answers 503.
 */
public final class FoundgineMcpSessionGate {
    public enum Action { PROCEED, REJECT }

    /** @param issuedSessionId set only when a new session was created and must be returned to the client */
    public record Outcome(Action action, int httpStatus, String issuedSessionId) {
        public boolean proceeds() { return action == Action.PROCEED; }
    }

    private final FoundgineMcpSessionManager sessions;
    private final FoundgineMcpAuditSink audit;
    private final LongSupplier clock;

    public FoundgineMcpSessionGate(FoundgineMcpSessionManager sessions) {
        this(sessions, FoundgineMcpAuditSink.NONE, System::currentTimeMillis);
    }

    /** Emits {@link FoundgineMcpAuditEvent}s for issued, rejected and terminated sessions, and for a full table. */
    public FoundgineMcpSessionGate(FoundgineMcpSessionManager sessions, FoundgineMcpAuditSink audit, LongSupplier clock) {
        this.sessions = Objects.requireNonNull(sessions, "sessions");
        this.audit = Objects.requireNonNull(audit, "audit");
        this.clock = Objects.requireNonNull(clock, "clock");
    }

    private void emit(FoundgineMcpAuditEvent.Type type, String subject, String owner, String detail) {
        FoundgineMcpAuditSink.emit(audit, new FoundgineMcpAuditEvent(type, clock.getAsLong(), subject, owner, detail));
    }

    /**
     * @param jsonRpcMethod the JSON-RPC {@code method} of the request body, or {@code null} if it could not be read
     * @param sessionHeader the raw {@code Mcp-Session-Id} header, or {@code null}
     * @param owner the authenticated principal of this request, or {@code null} when sessions are not owner-bound
     */
    public Outcome admitPost(String jsonRpcMethod, String sessionHeader, String owner) {
        if ("initialize".equals(jsonRpcMethod)) {
            try {
                String id = sessions.create(owner).sessionId();
                emit(FoundgineMcpAuditEvent.Type.SESSION_ISSUED, FoundgineMcpSessionManager.fingerprint(id), owner, null);
                return new Outcome(Action.PROCEED, 200, id);
            } catch (IllegalStateException full) {
                emit(FoundgineMcpAuditEvent.Type.SESSION_TABLE_FULL, null, owner, null);
                return new Outcome(Action.REJECT, 503, null);
            }
        }
        if (sessionHeader == null || sessionHeader.isBlank()) {
            emit(FoundgineMcpAuditEvent.Type.SESSION_REJECTED, null, owner, "method=POST status=MISSING");
            return new Outcome(Action.REJECT, 400, null);
        }
        var validation = sessions.validate(sessionHeader, owner);
        if (validation.isValid()) return new Outcome(Action.PROCEED, 200, null);
        emit(FoundgineMcpAuditEvent.Type.SESSION_REJECTED, FoundgineMcpSessionManager.fingerprint(sessionHeader), owner,
                "method=POST status=" + validation.status().name());
        return new Outcome(Action.REJECT, 404, null);
    }

    /**
     * Admits the GET that opens or resumes the server-to-client SSE stream: the session must exist and belong to this owner
     * (400 when the header is missing, 404 for anything else). Resume semantics ({@code Last-Event-ID}) belong to
     * {@link FoundgineMcpSseBuffer}, which the transport consults only after this returns {@link Action#PROCEED}.
     */
    public Outcome admitGet(String sessionHeader, String owner) {
        if (sessionHeader == null || sessionHeader.isBlank()) {
            emit(FoundgineMcpAuditEvent.Type.SESSION_REJECTED, null, owner, "method=GET status=MISSING");
            return new Outcome(Action.REJECT, 400, null);
        }
        var validation = sessions.validate(sessionHeader, owner);
        if (validation.isValid()) return new Outcome(Action.PROCEED, 200, null);
        emit(FoundgineMcpAuditEvent.Type.SESSION_REJECTED, FoundgineMcpSessionManager.fingerprint(sessionHeader), owner,
                "method=GET status=" + validation.status().name());
        return new Outcome(Action.REJECT, 404, null);
    }

    /** @see FoundgineMcpSessionManager#isLive */
    public boolean isLive(String sessionId) { return sessions.isLive(sessionId); }

    /** Removes expired sessions from the store; schedule it periodically. @return how many were removed */
    public int purgeExpired() { return sessions.purgeExpired(); }

    public Outcome admitDelete(String sessionHeader, String owner) {
        if (sessionHeader == null || sessionHeader.isBlank()) {
            emit(FoundgineMcpAuditEvent.Type.SESSION_REJECTED, null, owner, "method=DELETE status=MISSING");
            return new Outcome(Action.REJECT, 400, null);
        }
        String fingerprint = FoundgineMcpSessionManager.fingerprint(sessionHeader);
        if (sessions.terminate(sessionHeader, owner)) {
            emit(FoundgineMcpAuditEvent.Type.SESSION_TERMINATED, fingerprint, owner, null);
            return new Outcome(Action.PROCEED, 204, null);
        }
        emit(FoundgineMcpAuditEvent.Type.SESSION_REJECTED, fingerprint, owner, "method=DELETE status=NOT_LIVE");
        return new Outcome(Action.REJECT, 404, null);
    }
}
