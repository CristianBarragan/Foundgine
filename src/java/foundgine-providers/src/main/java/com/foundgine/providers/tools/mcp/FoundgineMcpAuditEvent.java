package com.foundgine.providers.tools.mcp;

/**
 * A security-relevant MCP event for the host's audit trail. Events never carry secrets: no key material, no session id (a
 * short digest fingerprint identifies a session instead, see {@link FoundgineMcpSessionManager#fingerprint}), no tokens.
 *
 * @param type what happened
 * @param atUnixMilliseconds when, by the emitting component's clock
 * @param subject the key id, or the session fingerprint; {@code null} when the subject was attacker-controlled garbage
 * @param actor the authenticated principal the request ran as, when known
 * @param detail short machine-readable context such as {@code status=EXPIRED}, {@code previous=k1}, {@code resume=true}
 *        (SSE_OPENED), {@code status=BUSY|EVICTED|INVALID|LIMIT|GONE} (SSE_REJECTED) or {@code reason=...} (SSE_CLOSED)
 */
public record FoundgineMcpAuditEvent(Type type, long atUnixMilliseconds, String subject, String actor, String detail) {
    public enum Type {
        KEY_ACTIVATED, KEY_ROTATED, KEY_ADDED, KEY_RETIRED, KEY_REVOKED,
        SESSION_ISSUED, SESSION_REJECTED, SESSION_TERMINATED, SESSION_TABLE_FULL,
        SSE_OPENED, SSE_REJECTED, SSE_CLOSED
    }
}
