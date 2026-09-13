package com.foundgine.samples.supplychain.advanced;

import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.domain.Domain.*;
import com.foundgine.samples.supplychain.advanced.scenarios.ScenariosAdversarialInvariants;
import org.junit.jupiter.api.Test;
import java.math.BigDecimal;
import java.time.LocalDate;
import java.util.Set;
import static org.junit.jupiter.api.Assertions.*;

/** Content-level parity with the C# AdversarialInvariantTests. */
class AdversarialInvariantParityTest {
    @Test void documented_seed_and_scoped_tenant_pass() {
        assertDoesNotThrow(() -> ScenariosAdversarialInvariants.assertAdversarialInvariants(
                SupplyChainData.seed(), new Authorization.Context("tenant-a", Set.of(1, 2), Authorization.Role.SUPPLY_CHAIN_MANAGER, false)));
    }

    @Test void cross_tenant_warehouse_in_authorization_context_fails_closed() {
        var ex = assertThrows(IllegalStateException.class, () -> ScenariosAdversarialInvariants.assertAdversarialInvariants(
                SupplyChainData.seed(), new Authorization.Context("tenant-a", Set.of(1, 2, 3), Authorization.Role.SUPPLY_CHAIN_MANAGER, false)));
        assertTrue(ex.getMessage().toLowerCase().contains("tenant-b"));
    }

    @Test void missing_bom_cycle_fixture_is_rejected() {
        var d = new SupplyChainData();
        d.suppliers.add(new Supplier(1, "Kiwi Components", "NZ", new BigDecimal("0.22"), "tenant-a"));
        d.products.add(new Product(1, "MOTOR-X", "Industrial Motor", "Motors", new BigDecimal("30"), new BigDecimal("12.25")));
        d.products.add(new Product(2, "CTRL-X", "Motor Controller", "Controls", new BigDecimal("40"), new BigDecimal("19.50")));
        d.components.add(new ProductComponent(1, 2, BigDecimal.ONE, null, null, "1", false, BigDecimal.ZERO, BigDecimal.ZERO));
        d.certifications.add(new SupplierCertification(1, 1, "ISO9001", LocalDate.of(2024, 1, 1), LocalDate.of(2024, 12, 31)));
        var ex = assertThrows(IllegalStateException.class, () -> ScenariosAdversarialInvariants.assertAdversarialInvariants(
                d, new Authorization.Context("tenant-a", Set.of(1), Authorization.Role.SUPPLY_CHAIN_MANAGER, false)));
        assertTrue(ex.getMessage().toLowerCase().contains("cycle"));
    }

    @Test void supplier_three_certification_not_expired_is_rejected() {
        var d = new SupplyChainData();
        d.suppliers.add(new Supplier(1, "Kiwi Components", "NZ", new BigDecimal("0.22"), "tenant-a"));
        d.suppliers.add(new Supplier(3, "Global Metals", "US", new BigDecimal("0.62"), "tenant-b"));
        d.products.add(new Product(1, "MOTOR-X", "Industrial Motor", "Motors", new BigDecimal("30"), new BigDecimal("12.25")));
        d.products.add(new Product(2, "CTRL-X", "Motor Controller", "Controls", new BigDecimal("40"), new BigDecimal("19.50")));
        d.products.add(new Product(3, "PCB-X", "Controller PCB", "Electronics", new BigDecimal("80"), new BigDecimal("26.75")));
        d.components.add(new ProductComponent(1, 2, BigDecimal.ONE, null, null, "1", false, BigDecimal.ZERO, BigDecimal.ZERO));
        d.components.add(new ProductComponent(2, 3, BigDecimal.ONE, null, null, "1", false, BigDecimal.ZERO, BigDecimal.ZERO));
        d.components.add(new ProductComponent(3, 1, BigDecimal.ONE, null, null, "1", false, BigDecimal.ZERO, BigDecimal.ZERO));
        d.certifications.add(new SupplierCertification(1, 3, "ISO9001", LocalDate.of(2026, 1, 1), LocalDate.of(2026, 8, 26)));
        var ex = assertThrows(IllegalStateException.class, () -> ScenariosAdversarialInvariants.assertAdversarialInvariants(
                d, new Authorization.Context("tenant-a", Set.of(1), Authorization.Role.SUPPLY_CHAIN_MANAGER, false)));
        assertTrue(ex.getMessage().toLowerCase().contains("expired certification"));
    }
}
