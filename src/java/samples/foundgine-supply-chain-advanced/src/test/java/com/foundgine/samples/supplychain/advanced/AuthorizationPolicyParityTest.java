package com.foundgine.samples.supplychain.advanced;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.abstractions.AuthorizationOperation;
import com.foundgine.core.abstractions.AuthorizationOperationName;
import com.foundgine.core.abstractions.AuthorizationPredicateKind;
import com.foundgine.samples.supplychain.advanced.authorization.SupplyChainAuthorization;
import com.foundgine.samples.supplychain.advanced.authorization.claims.Claims;

import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.Map;

/** Content-level parity with the C# AuthorizationPolicyTests. */
class AuthorizationPolicyParityTest {
    private static final Instant NOW = Instant.parse("2026-08-28T00:00:00Z");

    @Test
    void entityFieldRelationshipConditionalWriteAndNamedOperationPoliciesAreDistinct() {
        var analyst =
                SupplyChainAuthorization.create("tenant-a", SupplyChainAuthorization.Role.ANALYST);
        var operator =
                SupplyChainAuthorization.create(
                        "tenant-a", SupplyChainAuthorization.Role.WAREHOUSE_OPERATOR);

        assertTrue(
                analyst.canAccessEntity(
                        com.foundgine.samples.supplychain.advanced.semantics
                                .SupplyChainSemanticModel.PRODUCT));
        assertTrue(
                analyst.canAccessEntity(
                        com.foundgine.samples.supplychain.advanced.semantics
                                .SupplyChainSemanticModel.COMPLIANCE_INCIDENT));
        assertFalse(
                analyst.canAccessField(
                        com.foundgine.samples.supplychain.advanced.semantics
                                .SupplyChainSemanticModel.INVENTORY_LOT,
                        SupplyChainAuthorization.field("InventoryLot", "Quarantined")));
        assertFalse(
                operator.canAccessRelationship(
                        com.foundgine.samples.supplychain.advanced.semantics
                                .SupplyChainSemanticModel.SUPPLIER,
                        SupplyChainAuthorization.relationship("Supplier", "incidents")));
        assertNotNull(
                analyst.getPredicate(
                        com.foundgine.samples.supplychain.advanced.semantics
                                .SupplyChainSemanticModel.WAREHOUSE,
                        AuthorizationOperation.READ));
        assertFalse(
                analyst.getEntityAccess(
                                com.foundgine.samples.supplychain.advanced.semantics
                                        .SupplyChainSemanticModel.INVENTORY_LOT,
                                AuthorizationOperation.WRITE)
                        .isAllowed());
        assertFalse(
                operator.getEntityAccess(
                                com.foundgine.samples.supplychain.advanced.semantics
                                        .SupplyChainSemanticModel.INVENTORY_LOT,
                                AuthorizationOperation.WRITE,
                                new AuthorizationOperationName("inventory.reconcile"))
                        .isAllowed());
        assertTrue(
                operator.getEntityAccess(
                                com.foundgine.samples.supplychain.advanced.semantics
                                        .SupplyChainSemanticModel.INVENTORY_LOT,
                                AuthorizationOperation.WRITE,
                                new AuthorizationOperationName("update"))
                        .isAllowed());
    }

    @Test
    void identityClaimsAreNeverAcceptedAndFailClosed() {
        for (String key :
                new String[] {"role", "tenant", "tenantId", "actor", "isAdmin", "permissions"}) {
            var result = Claims.validate(Map.of(key, "SupplyChainManager"), NOW);
            assertTrue(result.isSpoofingAttempt(), key);
            assertTrue(result.accepted().isEmpty(), key);
            assertTrue(
                    result.rejected().stream().anyMatch(x -> x.key().equalsIgnoreCase(key)), key);
        }
    }

    @Test
    void identityClaimIsRejectedEvenWhenValueMatches() {
        var result = Claims.validate(Map.of("tenant", "tenant-a"), NOW);
        assertTrue(result.isSpoofingAttempt());
    }

    @Test
    void unrecognizedClaimsAreDroppedIndividually() {
        var result = Claims.validate(Map.of("favorite_color", "blue", "scope", "read-only"), NOW);
        assertFalse(result.isSpoofingAttempt());
        assertEquals("read-only", result.accepted().get("scope"));
        assertTrue(result.rejected().stream().anyMatch(x -> x.key().equals("favorite_color")));
    }

    @Test
    void malformedClaimsAreRejectedIndividually() {
        for (var entry :
                Map.of(
                                "scope", "full-access",
                                "warehouse", "-3",
                                "max_rows", "999999",
                                "reason", "short",
                                "change_ticket", "TICKET-1",
                                "not_after", "not-a-date")
                        .entrySet()) {
            var result = Claims.validate(Map.of(entry.getKey(), entry.getValue()), NOW);
            assertFalse(result.isSpoofingAttempt(), entry.getKey());
            assertFalse(result.accepted().containsKey(entry.getKey()), entry.getKey());
            assertTrue(
                    result.rejected().stream().anyMatch(x -> x.key().equals(entry.getKey())),
                    entry.getKey());
        }
        for (var entry :
                Map.of(
                                "warehouse",
                                "999999999999999999999999",
                                "max_rows",
                                "999999999999999999999999")
                        .entrySet()) {
            var result = Claims.validate(Map.of(entry.getKey(), entry.getValue()), NOW);
            assertFalse(result.isSpoofingAttempt(), entry.getKey());
            assertFalse(result.accepted().containsKey(entry.getKey()), entry.getKey());
            assertTrue(
                    result.rejected().stream().anyMatch(x -> x.key().equals(entry.getKey())),
                    entry.getKey());
        }
    }

    @Test
    void expiredEvidenceIsRetracted() {
        var result =
                Claims.validate(
                        Map.of(
                                "reason", "Quarterly cycle count discrepancy",
                                "change_ticket", "CHG-4821",
                                "not_after", "2020-01-01T00:00:00Z"),
                        NOW);
        assertFalse(result.isSpoofingAttempt());
        assertFalse(result.accepted().containsKey("reason"));
        assertFalse(result.accepted().containsKey("change_ticket"));
        assertFalse(result.accepted().containsKey("not_after"));
    }

    @Test
    void wellFormedEvidenceWithoutExpiryIsAccepted() {
        var result =
                Claims.validate(
                        Map.of(
                                "reason", "Quarterly cycle count discrepancy",
                                "change_ticket", "CHG-4821"),
                        NOW);
        assertFalse(result.isSpoofingAttempt());
        assertEquals("CHG-4821", result.accepted().get("change_ticket"));
        assertTrue(result.rejected().isEmpty());
    }

    @Test
    void readOnlyClaimNarrowsManagerWriteAccess() {
        var validated = Claims.validate(Map.of("scope", "read-only"), NOW);
        var manager =
                SupplyChainAuthorization.create(
                        "tenant-a",
                        SupplyChainAuthorization.Role.SUPPLY_CHAIN_MANAGER,
                        validated.accepted());
        var unrestricted =
                SupplyChainAuthorization.create(
                        "tenant-a", SupplyChainAuthorization.Role.SUPPLY_CHAIN_MANAGER);
        assertFalse(
                manager.canWriteEntity(
                        com.foundgine.samples.supplychain.advanced.semantics
                                .SupplyChainSemanticModel.INVENTORY_LOT));
        assertTrue(
                unrestricted.canWriteEntity(
                        com.foundgine.samples.supplychain.advanced.semantics
                                .SupplyChainSemanticModel.INVENTORY_LOT));
    }

    @Test
    void warehouseScopeIsAndedWithTenantPredicateOnWarehouse() {
        var validated = Claims.validate(Map.of("warehouse", "12"), NOW);
        var policy =
                SupplyChainAuthorization.create(
                        "tenant-a", SupplyChainAuthorization.Role.ANALYST, validated.accepted());
        var predicate =
                policy.getPredicate(
                        com.foundgine.samples.supplychain.advanced.semantics
                                .SupplyChainSemanticModel.WAREHOUSE,
                        AuthorizationOperation.READ);
        assertNotNull(predicate);
        assertEquals(AuthorizationPredicateKind.AND, predicate.kind());
    }

    @Test
    void warehouseScopeAloneConstrainsInventoryLotWithoutInventedTenantPredicate() {
        var policy =
                SupplyChainAuthorization.create(
                        "tenant-a",
                        SupplyChainAuthorization.Role.ANALYST,
                        Map.of("warehouse", "12"));
        var predicate =
                policy.getPredicate(
                        com.foundgine.samples.supplychain.advanced.semantics
                                .SupplyChainSemanticModel.INVENTORY_LOT,
                        AuthorizationOperation.READ);
        assertNotNull(predicate);
        assertEquals(AuthorizationPredicateKind.EQUAL, predicate.kind());
    }

    @Test
    void reconcileRequiresManagerAndValidEvidence() {
        var model =
                com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel
                        .INVENTORY_LOT;
        var noEvidence =
                SupplyChainAuthorization.create(
                        "tenant-a", SupplyChainAuthorization.Role.SUPPLY_CHAIN_MANAGER);
        assertFalse(
                noEvidence
                        .getEntityAccess(
                                model,
                                AuthorizationOperation.WRITE,
                                new AuthorizationOperationName("inventory.reconcile"))
                        .isAllowed());

        var evidence =
                Claims.validate(
                        Map.of(
                                "reason",
                                "Quarterly cycle count discrepancy",
                                "change_ticket",
                                "CHG-4821"),
                        NOW);
        var manager =
                SupplyChainAuthorization.create(
                        "tenant-a",
                        SupplyChainAuthorization.Role.SUPPLY_CHAIN_MANAGER,
                        evidence.accepted());
        assertTrue(
                manager.getEntityAccess(
                                model,
                                AuthorizationOperation.WRITE,
                                new AuthorizationOperationName("inventory.reconcile"))
                        .isAllowed());

        var operator =
                SupplyChainAuthorization.create(
                        "tenant-a",
                        SupplyChainAuthorization.Role.WAREHOUSE_OPERATOR,
                        evidence.accepted());
        assertFalse(
                operator.getEntityAccess(
                                model,
                                AuthorizationOperation.WRITE,
                                new AuthorizationOperationName("inventory.reconcile"))
                        .isAllowed());
    }
}
