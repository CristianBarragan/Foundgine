package com.foundgine.core.semantic.security.warrants;

import org.junit.jupiter.api.Test;
import java.time.Instant;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

class WarrantTrustBoundaryParityTest {
    private static SecurityWarrant warrant(String issuer) {
        var now = Instant.now();
        return SecurityWarrant.ofDefaults("warrant-1", issuer, "agent-a", "foundgine",
                List.of(new CapabilityGrant("Customer.read", "read", List.of("customer/*"))),
                new SecurityWarrantConstraints(), now.minusSeconds(60), now.plusSeconds(3600),
                "nonce-1", "key-1", null, new byte[0]);
    }

    @Test
    void missingTrustedIssuerFailsClosed() {
        var w = warrant("attacker-controlled-issuer");
        assertThrows(IllegalStateException.class, () ->
                SecurityWarrantVerifier.verify(w, keyId -> null, Instant.now(), null));
    }

    @Test
    void expiredWarrantFailsRevocationGuard() {
        var now = Instant.now();
        var w = SecurityWarrant.ofDefaults("w", "issuer", "subject", "audience", List.of(),
                new SecurityWarrantConstraints(), now.minusSeconds(120), now.minusSeconds(1),
                "nonce", "key", null, new byte[0]);
        var store = new MemorySecurityWarrantRevocationStore();
        assertThrows(IllegalStateException.class, () ->
                SecurityWarrantRevocationGuard.validate(w, store, now));
    }

    @Test
    void replayConsumptionIsRejectedBySameMemoryStore() {
        var w = warrant("issuer");
        var store = new MemorySecurityWarrantReplayStore();
        SecurityWarrantReplayGuard.consume(w, store, Instant.now());
        assertThrows(IllegalStateException.class, () ->
                SecurityWarrantReplayGuard.consume(w, store, Instant.now()));
    }
}
