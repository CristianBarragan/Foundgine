package com.foundgine.core.semantic.security.warrants;

import java.time.Instant;

/**
 * Single-use replay protection for a signed warrant nonce.
 *
 * <p>Implementations must be atomic: for one (warrantId, nonce) pair exactly one caller, on any host sharing the
 * store, may receive {@code true}.
 */
public interface ISecurityWarrantReplayStore {
    /** Records the pair with no known lifetime. Stores cannot expire such entries. */
    boolean tryConsume(String warrantId, String nonce);

    /**
     * Records the pair until the warrant expires so the store can discard it afterwards. An expired warrant is rejected
     * by the time check before it reaches the store, so retaining an entry past {@code expiresAt} (plus clock skew) has
     * no security value. The default ignores the expiry and delegates, so existing custom stores keep working; override
     * it to bound storage (for example a Redis TTL).
     */
    default boolean tryConsume(String warrantId, String nonce, Instant expiresAt) {
        return tryConsume(warrantId, nonce);
    }
}
