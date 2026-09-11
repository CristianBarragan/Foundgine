package com.foundgine.benchmarks.highassurance.postgres;

import com.foundgine.benchmarks.highassurance.TransferFunds;
import org.junit.jupiter.api.Test;

import java.math.BigDecimal;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertThrows;

class AuthorizationExecutionBindingParityTest {
    private static final UUID ACTOR = UUID.fromString("11111111-1111-1111-1111-111111111111");
    private static final UUID SOURCE = UUID.fromString("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static final UUID DESTINATION = UUID.fromString("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static TransferFunds.Command command(BigDecimal amount, String key) {
        return new TransferFunds.Command(SOURCE, DESTINATION, amount, key);
    }

    private static PostgresAuthorizationDecision evidence(long version, String fingerprint) {
        return new PostgresAuthorizationDecision(true, version, fingerprint);
    }

    @Test
    void exactRequestAndEvidenceAreBound() {
        var command = command(new BigDecimal("10"), "k-1");
        var binding = AuthorizationExecutionBinding.create(ACTOR, 7, command, evidence(7, "fp-7"));
        assertDoesNotThrow(() -> binding.validateAgainst(ACTOR, 7, command, evidence(7, "fp-7")));
    }

    @Test
    void differentAmountCannotReuseAuthorizationEvidence() {
        var binding = AuthorizationExecutionBinding.create(ACTOR, 7, command(new BigDecimal("10"), "k-1"), evidence(7, "fp-7"));
        assertThrows(IllegalStateException.class,
                () -> binding.validateAgainst(ACTOR, 7, command(new BigDecimal("11"), "k-1"), evidence(7, "fp-7")));
    }

    @Test
    void differentIdempotencyKeyCannotReuseAuthorizationEvidence() {
        var binding = AuthorizationExecutionBinding.create(ACTOR, 7, command(new BigDecimal("10"), "k-1"), evidence(7, "fp-7"));
        assertThrows(IllegalStateException.class,
                () -> binding.validateAgainst(ACTOR, 7, command(new BigDecimal("10"), "k-2"), evidence(7, "fp-7")));
    }

    @Test
    void differentResourceCannotReuseAuthorizationEvidence() {
        var binding = AuthorizationExecutionBinding.create(ACTOR, 7, command(new BigDecimal("10"), "k-1"), evidence(7, "fp-7"));
        var different = new TransferFunds.Command(
                UUID.fromString("cccccccc-cccc-cccc-cccc-cccccccccccc"), DESTINATION,
                new BigDecimal("10"), "k-1");
        assertThrows(IllegalStateException.class,
                () -> binding.validateAgainst(ACTOR, 7, different, evidence(7, "fp-7")));
    }

    @Test
    void differentActorOrTenantCannotReuseBinding() {
        var command = command(new BigDecimal("10"), "k-1");
        var binding = AuthorizationExecutionBinding.create(ACTOR, 7, command, evidence(7, "fp-7"));
        assertThrows(IllegalStateException.class, () -> binding.validateAgainst(
                UUID.fromString("22222222-2222-2222-2222-222222222222"), 7, command, evidence(7, "fp-7")));
        assertThrows(IllegalStateException.class, () -> binding.validateAgainst(ACTOR, 8, command, evidence(7, "fp-7")));
    }

    @Test
    void differentAuthorizationVersionCannotCrossExecutionGate() {
        var command = command(new BigDecimal("10"), "k-1");
        var binding = AuthorizationExecutionBinding.create(ACTOR, 7, command, evidence(7, "fp-7"));
        assertThrows(IllegalStateException.class,
                () -> binding.validateAgainst(ACTOR, 7, command, evidence(8, "fp-8")));
    }

    @Test
    void differentAuthorizationFingerprintCannotCrossExecutionGate() {
        var command = command(new BigDecimal("10"), "k-1");
        var binding = AuthorizationExecutionBinding.create(ACTOR, 7, command, evidence(7, "fp-7"));
        assertThrows(IllegalStateException.class,
                () -> binding.validateAgainst(ACTOR, 7, command, evidence(7, "forged")));
    }

    @Test
    void deniedEvidenceCannotBeBound() {
        var denied = new PostgresAuthorizationDecision(false, 7, "fp-7");
        assertThrows(SecurityException.class,
                () -> AuthorizationExecutionBinding.create(ACTOR, 7, command(new BigDecimal("10"), "k-1"), denied));
    }
}
