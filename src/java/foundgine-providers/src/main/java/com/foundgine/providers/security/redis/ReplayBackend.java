package com.foundgine.providers.security.redis;

import java.time.Duration;

/**
 * The one operation the shared replay store needs from a Redis-protocol server (Redis, Valkey, Microsoft Garnet, which is
 * built on FASTER, or any compatible service).
 */
@FunctionalInterface
public interface ReplayBackend {
    /**
     * Atomically stores {@code key} for {@code ttl} only if it does not exist: {@code SET key 1 NX PX ttl}.
     *
     * @return {@code true} if this call created the key; {@code false} if it already existed
     * @throws RuntimeException if the server cannot be reached or answers with an error; the caller treats that as a
     *     denial, never as success
     */
    boolean setIfAbsent(String key, Duration ttl);
}
