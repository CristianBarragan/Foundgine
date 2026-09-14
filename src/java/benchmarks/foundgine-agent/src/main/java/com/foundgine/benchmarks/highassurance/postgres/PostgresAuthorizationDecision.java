package com.foundgine.benchmarks.highassurance.postgres;

import java.util.Objects;

/** Versioned execution-time authorization evidence. */
public record PostgresAuthorizationDecision(boolean allowed, long version, String fingerprint) {
    public PostgresAuthorizationDecision {
        if (version < 0) throw new IllegalArgumentException("Authorization version cannot be negative.");
        if (fingerprint == null || fingerprint.isBlank()) throw new IllegalArgumentException("Authorization fingerprint is required.");
    }
}
