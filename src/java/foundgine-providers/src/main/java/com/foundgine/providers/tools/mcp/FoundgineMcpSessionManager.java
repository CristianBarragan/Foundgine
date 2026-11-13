package com.foundgine.providers.tools.mcp;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.security.SecureRandom;
import java.time.Duration;
import java.util.Base64;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.ConcurrentHashMap;
import java.util.function.LongSupplier;

/**
 * MCP session lifecycle: opaque identifiers, idle and absolute expiry, validation, and explicit termination.
 *
 * <p><b>A session is not authentication.</b> Holding a session id proves nothing about who is calling. Every request must
 * still be authenticated by the host and every tool still reads its identity from the host's security context provider. A
 * valid session never produces or substitutes a security context, an approval or a warrant. To make a leaked id useless to
 * another principal, a session may be bound to an opaque {@code owner} (typically the authenticated subject) when it is
 * created; validation and termination then require the same owner. That binding is additional defence, not a replacement
 * for authenticating the request.
 *
 * <p><b>Identifiers</b> are 256 random bits from {@link SecureRandom}, base64url without padding (43 characters from the
 * visible-ASCII set the MCP specification requires). The store never sees the id: it is keyed by a SHA-256 digest, so a
 * leaked store cannot be replayed. Malformed ids are rejected before any lookup.
 *
 * <p><b>Expiry.</b> A session has an idle timeout that slides on every successful validation and an absolute maximum
 * lifetime that never slides. Expired sessions are removed when they are found and by {@link #purgeExpired()}, which the
 * host should call periodically. The number of live sessions is capped.
 *
 * <p><b>HTTP mapping</b> (for the transport owner, header {@link #HEADER_NAME}): issue the id in the response to
 * {@code initialize}; for later requests treat every status other than {@link Status#VALID} identically (HTTP 404, the client
 * must re-initialize) so nothing is revealed about why; answer {@code DELETE} with {@link #terminate}.
 */
public final class FoundgineMcpSessionManager {
    /** The MCP streamable-HTTP header that carries the session id. */
    public static final String HEADER_NAME = "Mcp-Session-Id";
    static final int ID_LENGTH = 43;
    private static final int ID_BYTES = 32;

    public enum Status { VALID, NOT_FOUND, EXPIRED, OWNER_MISMATCH }

    /** Lifetimes and capacity. */
    public record Options(Duration idleTimeout, Duration maximumLifetime, int maximumActiveSessions) {
        public Options {
            Objects.requireNonNull(idleTimeout, "idleTimeout");
            Objects.requireNonNull(maximumLifetime, "maximumLifetime");
            if (idleTimeout.toMillis() <= 0) throw new IllegalArgumentException("Idle timeout must be positive.");
            if (maximumLifetime.compareTo(idleTimeout) < 0)
                throw new IllegalArgumentException("Maximum lifetime cannot be shorter than the idle timeout.");
            if (maximumActiveSessions <= 0) throw new IllegalArgumentException("Maximum active sessions must be positive.");
        }

        /** 30 minutes idle, 24 hours absolute, 10 000 sessions. */
        public static Options defaults() { return new Options(Duration.ofMinutes(30), Duration.ofHours(24), 10_000); }
    }

    /** A validated session as seen by the caller. {@code expiresAt} is the earlier of the idle and absolute deadlines. */
    public record Session(String sessionId, long createdAtUnixMilliseconds, long lastAccessedAtUnixMilliseconds,
                          long expiresAtUnixMilliseconds) {}

    public record Validation(Status status, Session session) {
        public boolean isValid() { return status == Status.VALID; }
    }

    /** What a store keeps. {@code key} is a digest of the id; {@code ownerDigest} is {@code null} for unbound sessions. */
    public record StoredSession(String key, String ownerDigest, long createdAtUnixMilliseconds,
                                long lastAccessedAtUnixMilliseconds, long idleExpiresAtUnixMilliseconds,
                                long absoluteExpiresAtUnixMilliseconds) {
        public long expiresAt() { return Math.min(idleExpiresAtUnixMilliseconds, absoluteExpiresAtUnixMilliseconds); }
    }

    /**
     * Host-replaceable session storage. A clustered deployment needs a shared implementation; every operation must be
     * atomic.
     */
    public interface Store {
        /** Adds the session unless the key exists; returns false on a collision. */
        boolean tryAdd(StoredSession session);
        /** The stored session or {@code null}. */
        StoredSession find(String key);
        /**
         * Atomically records an access on an existing session and returns it, or {@code null} if it no longer exists. It must
         * never create a session and never move the idle deadline backwards.
         */
        StoredSession touch(String key, long lastAccessedAtUnixMilliseconds, long idleExpiresAtUnixMilliseconds);
        /** Removes the session; returns whether it existed. */
        boolean remove(String key);
        /** Removes sessions whose earlier deadline is at or before {@code nowUnixMilliseconds}; returns how many. */
        int removeExpired(long nowUnixMilliseconds);
        int count();
    }

    /** Process-local store, suitable for single-instance hosts and tests. */
    public static final class InMemoryStore implements Store {
        private final Map<String, StoredSession> sessions = new ConcurrentHashMap<>();

        @Override public boolean tryAdd(StoredSession s) { return sessions.putIfAbsent(s.key(), s) == null; }
        @Override public StoredSession find(String key) { return sessions.get(key); }
        @Override public StoredSession touch(String key, long accessed, long idleExpires) {
            return sessions.computeIfPresent(key, (k, s) -> new StoredSession(k, s.ownerDigest(),
                    s.createdAtUnixMilliseconds(), Math.max(accessed, s.lastAccessedAtUnixMilliseconds()),
                    Math.max(idleExpires, s.idleExpiresAtUnixMilliseconds()), s.absoluteExpiresAtUnixMilliseconds()));
        }
        @Override public boolean remove(String key) { return sessions.remove(key) != null; }
        @Override public int removeExpired(long now) {
            int removed = 0;   // count only what this call removed; a size difference would include other threads' work
            for (var e : sessions.entrySet())
                if (e.getValue().expiresAt() <= now && sessions.remove(e.getKey(), e.getValue())) removed++;
            return removed;
        }
        @Override public int count() { return sessions.size(); }
    }

    private final Options options;
    private final Store store;
    private final LongSupplier clock;
    private final SecureRandom random = new SecureRandom();
    private final Object createLock = new Object();

    public FoundgineMcpSessionManager() { this(Options.defaults(), new InMemoryStore(), System::currentTimeMillis); }

    public FoundgineMcpSessionManager(Options options, Store store, LongSupplier clock) {
        this.options = Objects.requireNonNull(options, "options");
        this.store = Objects.requireNonNull(store, "store");
        this.clock = Objects.requireNonNull(clock, "clock");
    }

    /**
     * Creates a session. Call it only for a request the host has already authenticated.
     *
     * @param owner opaque principal to bind the session to, or {@code null} for no binding
     * @throws IllegalStateException when the active-session cap is reached and nothing has expired
     */
    public Session create(String owner) {
        String ownerDigest = owner == null ? null : digest(owner);
        synchronized (createLock) {
            long now = clock.getAsLong();
            if (store.count() >= options.maximumActiveSessions()) {
                store.removeExpired(now);
                if (store.count() >= options.maximumActiveSessions())
                    throw new IllegalStateException("The MCP session limit has been reached.");
            }
            long absolute = saturatingAdd(now, options.maximumLifetime().toMillis());
            long idle = Math.min(saturatingAdd(now, options.idleTimeout().toMillis()), absolute);
            for (int attempt = 0; attempt < 3; attempt++) {
                String id = newId();
                if (store.tryAdd(new StoredSession(digest(id), ownerDigest, now, now, idle, absolute)))
                    return new Session(id, now, now, idle);
            }
            throw new IllegalStateException("Unable to allocate an MCP session id.");
        }
    }

    /**
     * Checks the id and, if valid, slides the idle deadline. Never throws for bad input. A request that is not
     * {@link Status#VALID} must be refused with a uniform "unknown session" response.
     *
     * @param owner the authenticated principal of <em>this</em> request; required if the session was bound
     */
    public Validation validate(String sessionId, String owner) {
        if (!isWellFormed(sessionId)) return new Validation(Status.NOT_FOUND, null);
        String key = digest(sessionId);
        StoredSession stored = store.find(key);
        if (stored == null) return new Validation(Status.NOT_FOUND, null);
        long now = clock.getAsLong();
        if (!ownerMatches(stored, owner)) return new Validation(Status.OWNER_MISMATCH, null);
        if (now >= stored.expiresAt()) {
            store.remove(key);
            return new Validation(Status.EXPIRED, null);
        }
        long idle = Math.min(saturatingAdd(now, options.idleTimeout().toMillis()), stored.absoluteExpiresAtUnixMilliseconds());
        StoredSession touched = store.touch(key, now, idle);
        if (touched == null) return new Validation(Status.NOT_FOUND, null);
        return new Validation(Status.VALID, new Session(sessionId, touched.createdAtUnixMilliseconds(),
                touched.lastAccessedAtUnixMilliseconds(), touched.expiresAt()));
    }

    /**
     * Terminates a session (the MCP {@code DELETE}). Idempotent. Only a matching owner can terminate a bound session.
     *
     * @return {@code true} if this call ended a live session
     */
    public boolean terminate(String sessionId, String owner) {
        if (!isWellFormed(sessionId)) return false;
        String key = digest(sessionId);
        StoredSession stored = store.find(key);
        if (stored == null || !ownerMatches(stored, owner)) return false;
        boolean live = clock.getAsLong() < stored.expiresAt();
        return store.remove(key) && live;
    }

    /**
     * Whether the session exists and has not expired, without sliding its idle deadline or checking an owner. For host-internal
     * housekeeping (such as dropping the resources of ended sessions); never use it to admit a request.
     */
    public boolean isLive(String sessionId) {
        if (!isWellFormed(sessionId)) return false;
        StoredSession stored = store.find(digest(sessionId));
        return stored != null && clock.getAsLong() < stored.expiresAt();
    }

    /** Removes expired sessions; schedule it periodically. @return how many were removed */
    public int purgeExpired() { return store.removeExpired(clock.getAsLong()); }

    /** Sessions currently stored, including expired ones not yet purged. */
    public int activeSessionCount() { return store.count(); }

    /**
     * A short, non-reversible identifier for audit logs. It is derived from the same digest the store keys on, so it matches
     * what a store operator sees, but it cannot be used to present a session. Returns {@code null} for ids that are not
     * well formed, so attacker-controlled text never reaches a log.
     */
    public static String fingerprint(String sessionId) { return isWellFormed(sessionId) ? digest(sessionId).substring(0, 12) : null; }

    /** True if {@code id} has the shape this manager issues; a cheap pre-check for transports. */
    public static boolean isWellFormed(String id) {
        if (id == null || id.length() != ID_LENGTH) return false;
        for (int i = 0; i < id.length(); i++) {
            char c = id.charAt(i);
            boolean ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
            if (!ok) return false;
        }
        return true;
    }

    private static boolean ownerMatches(StoredSession stored, String owner) {
        if (stored.ownerDigest() == null) return true;
        if (owner == null) return false;
        return MessageDigest.isEqual(stored.ownerDigest().getBytes(StandardCharsets.US_ASCII),
                digest(owner).getBytes(StandardCharsets.US_ASCII));
    }

    private String newId() {
        byte[] bytes = new byte[ID_BYTES];
        random.nextBytes(bytes);
        return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
    }

    private static String digest(String value) {
        try {
            byte[] hash = MessageDigest.getInstance("SHA-256").digest(value.getBytes(StandardCharsets.UTF_8));
            return Base64.getUrlEncoder().withoutPadding().encodeToString(hash);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-256 is unavailable.", e);
        }
    }

    private static long saturatingAdd(long a, long b) {
        try { return Math.addExact(a, b); }
        catch (ArithmeticException ignored) { return Long.MAX_VALUE; }
    }
}
