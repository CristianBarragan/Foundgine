package com.foundgine.core.security.penetration;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.semantic.security.warrants.*;

import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Instant;
import java.util.List;

/**
 * Hostile parity coverage for the warrant trust boundary itself.
 *
 * <p>These tests deliberately stop at the trust-configuration gates before cryptographic
 * verification: the important invariant is that an execution host never falls back to implicit
 * issuer or delegation trust.
 */
class WarrantTrustBoundaryPenetrationParityTest {

    private static SecurityWarrant warrant(String issuer) {
        var now = Instant.now();
        return SecurityWarrant.ofDefaults(
                "warrant-1",
                issuer,
                "agent-a",
                "foundgine",
                List.of(new CapabilityGrant("Customer.read", "read", List.of("customer/*"))),
                new SecurityWarrantConstraints(),
                now.minusSeconds(60),
                now.plusSeconds(3600),
                "nonce-1",
                "key-1",
                null,
                new byte[0]);
    }

    @Test
    void forgedIssuerIsRejectedWhenExpectedIssuerIsConfigured() {
        var forged = warrant("attacker-controlled-issuer");

        assertThrows(
                IllegalStateException.class,
                () ->
                        SecurityWarrantVerifier.verify(
                                forged, keyId -> null, Instant.now(), "trusted-root-issuer"));
    }

    @Test
    void forgedIssuerIsRejectedWhenExpectedIssuerIsUnconfigured() {
        var forged = warrant("attacker-controlled-issuer");

        assertThrows(
                IllegalStateException.class,
                () -> SecurityWarrantVerifier.verify(forged, keyId -> null, Instant.now(), null));
    }

    @Test
    void delegatedWarrantWithoutCompleteChainIsRejected() {
        var now = Instant.now();
        var uncheckedChild =
                SecurityWarrant.ofDefaults(
                        "child-1",
                        "root-issuer",
                        "agent-a",
                        "foundgine",
                        List.of(
                                new CapabilityGrant(
                                        "Customer.read", "read", List.of("customer/*"))),
                        new SecurityWarrantConstraints(),
                        now.minusSeconds(60),
                        now.plusSeconds(3600),
                        "nonce-child",
                        "key-1",
                        "never-verified-parent",
                        new byte[0]);

        assertThrows(
                IllegalStateException.class,
                () ->
                        SecurityWarrantExecutionTrust.verify(
                                uncheckedChild,
                                keyId -> null,
                                "root-issuer",
                                "foundgine",
                                now,
                                null,
                                null,
                                null));
    }

    @Test
    void independentMemoryReplayStoresDoNotPretendToBeDistributed() {
        var w = warrant("issuer");
        var instanceA = new MemorySecurityWarrantReplayStore();
        var instanceB = new MemorySecurityWarrantReplayStore();

        SecurityWarrantReplayGuard.consume(w, instanceA, Instant.now());

        // This is an intentional characterization test: process-local memory
        // state is not a distributed replay barrier.
        assertDoesNotThrow(() -> SecurityWarrantReplayGuard.consume(w, instanceB, Instant.now()));
    }

    @Test
    void durableReplayStoreSharesConsumptionAcrossInstances() throws IOException {
        Path path = Files.createTempFile("foundgine-replay-", ".log");
        Path lockPath = Path.of(path + ".lock");
        try {
            var instanceA = new FileSecurityWarrantReplayStore(path.toString());
            var instanceB = new FileSecurityWarrantReplayStore(path.toString());

            assertTrue(instanceA.tryConsume("warrant", "nonce"));
            assertFalse(instanceB.tryConsume("warrant", "nonce"));
        } finally {
            Files.deleteIfExists(path);
            Files.deleteIfExists(lockPath);
        }
    }
}
