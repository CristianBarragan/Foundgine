package com.foundgine.benchmarks.highassurance.postgres;

import com.foundgine.core.semantic.security.SecurityInvariantIds;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

class PostgresMutationSecurityConformanceParityTest {

    @Test
    void transferFundsDeclaresAllHighAssuranceInvariants() {
        var contract = PostgresMutationSecurityConformance.TRANSFER_FUNDS;

        assertTrue(contract.requiredInvariants()
                .contains(SecurityInvariantIds.TENANT_ISOLATION));
        assertTrue(contract.requiredInvariants()
                .contains(SecurityInvariantIds.RUNTIME_AUTHORIZATION));
        assertTrue(contract.requiredInvariants()
                .contains(SecurityInvariantIds.AUTHORIZATION_OWNERSHIP));
        assertTrue(contract.requiredInvariants()
                .contains(SecurityInvariantIds.MUTATION_DAILY_LIMIT));
        assertTrue(contract.requiredInvariants()
                .contains(SecurityInvariantIds.ATOMIC_MUTATION));
        assertTrue(contract.requiredInvariants()
                .contains(SecurityInvariantIds.MUTATION_ROW_LOCKING));
        assertTrue(contract.requiredInvariants()
                .contains(SecurityInvariantIds.IDEMPOTENCY));
        assertTrue(contract.requiredInvariants()
                .contains(SecurityInvariantIds.REPLAY_PROTECTION));
        assertTrue(contract.requiredInvariants()
                .contains(SecurityInvariantIds.AUDIT_REQUIRED));
        assertTrue(contract.requiredInvariants()
                .contains(SecurityInvariantIds.EXECUTION_EVIDENCE_REQUIRED));

        assertTrue(contract.isSatisfied());
        assertTrue(contract.missingRequirements().isEmpty());
    }

    @Test
    void every_high_assurance_obligation_fails_closed_when_removed() {
        var cases = new Object[][] {
                {
                        "transaction",
                        PostgresMutationSecurityConformance.TRANSFER_FUNDS
                                .withSingleTransaction(false),
                        SecurityInvariantIds.ATOMIC_MUTATION
                },
                {
                        "locking",
                        PostgresMutationSecurityConformance.TRANSFER_FUNDS
                                .withLocks(false),
                        SecurityInvariantIds.MUTATION_ROW_LOCKING
                },
                {
                        "authorization",
                        PostgresMutationSecurityConformance.TRANSFER_FUNDS
                                .withRuntimeAuthorization(false),
                        SecurityInvariantIds.RUNTIME_AUTHORIZATION
                },
                {
                        "idempotency",
                        PostgresMutationSecurityConformance.TRANSFER_FUNDS
                                .withIdempotency(false),
                        SecurityInvariantIds.IDEMPOTENCY
                },
                {
                        "replay",
                        PostgresMutationSecurityConformance.TRANSFER_FUNDS
                                .withReplay(false),
                        SecurityInvariantIds.REPLAY_PROTECTION
                },
                {
                        "audit",
                        PostgresMutationSecurityConformance.TRANSFER_FUNDS
                                .withAudit(false),
                        SecurityInvariantIds.AUDIT_REQUIRED
                },
                {
                        "receipt",
                        PostgresMutationSecurityConformance.TRANSFER_FUNDS
                                .withReceipt(false),
                        SecurityInvariantIds.EXECUTION_EVIDENCE_REQUIRED
                },
                {
                        "ownership",
                        PostgresMutationSecurityConformance.TRANSFER_FUNDS
                                .withOwnership(false),
                        SecurityInvariantIds.AUTHORIZATION_OWNERSHIP
                },
                {
                        "daily-limit",
                        PostgresMutationSecurityConformance.TRANSFER_FUNDS
                                .withDailyLimit(false),
                        SecurityInvariantIds.MUTATION_DAILY_LIMIT
                },
                {
                        "tenant",
                        PostgresMutationSecurityConformance.TRANSFER_FUNDS
                                .withTenantIsolation(false),
                        SecurityInvariantIds.TENANT_ISOLATION
                }
        };

        for (var row : cases) {
            var contract =
                    (PostgresMutationSecurityConformance) row[1];

            var name = (String) row[0];
            var invariant = (String) row[2];

            assertFalse(
                    contract.isSatisfied(),
                    name);

            assertTrue(
                    contract.missingRequirements().contains(invariant),
                    name);

            assertThrows(
                    IllegalStateException.class,
                    contract::ensureSatisfied,
                    name);
        }
    }
}