package com.foundgine.samples.supplychain.advanced;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.samples.supplychain.advanced.authorization.SupplyChainAuthorization;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;

import org.junit.jupiter.api.Test;

/** Exhaustive content parity with the C# SensitiveFieldAuthorizationTests. */
class SensitiveFieldAuthorizationParityTest {
    @Test
    void supplierRiskScoreIsAnalystOrManagerOnly() {
        var field = SupplyChainAuthorization.field("Supplier", "RiskScore");
        for (var role : SupplyChainAuthorization.Role.values()) {
            boolean expected =
                    role == SupplyChainAuthorization.Role.ANALYST
                            || role == SupplyChainAuthorization.Role.SUPPLY_CHAIN_MANAGER;
            assertEquals(
                    expected,
                    SupplyChainAuthorization.create("tenant-a", role)
                            .canAccessField(SupplyChainSemanticModel.SUPPLIER, field),
                    role.name());
        }
    }

    @Test
    void inventoryQuarantinedIsOperatorOrManagerOnly() {
        var field = SupplyChainAuthorization.field("InventoryLot", "Quarantined");
        for (var role : SupplyChainAuthorization.Role.values()) {
            boolean expected =
                    role == SupplyChainAuthorization.Role.WAREHOUSE_OPERATOR
                            || role == SupplyChainAuthorization.Role.SUPPLY_CHAIN_MANAGER;
            assertEquals(
                    expected,
                    SupplyChainAuthorization.create("tenant-a", role)
                            .canAccessField(SupplyChainSemanticModel.INVENTORY_LOT, field),
                    role.name());
        }
    }

    @Test
    void ordinaryInventoryFieldsRemainReadable() {
        for (var role : SupplyChainAuthorization.Role.values()) {
            var policy = SupplyChainAuthorization.create("tenant-a", role);
            assertTrue(
                    policy.canAccessField(
                            SupplyChainSemanticModel.INVENTORY_LOT,
                            SupplyChainAuthorization.field("InventoryLot", "OnHand")),
                    role.name());
            assertTrue(
                    policy.canAccessField(
                            SupplyChainSemanticModel.INVENTORY_LOT,
                            SupplyChainAuthorization.field("InventoryLot", "Reserved")),
                    role.name());
        }
    }
}
