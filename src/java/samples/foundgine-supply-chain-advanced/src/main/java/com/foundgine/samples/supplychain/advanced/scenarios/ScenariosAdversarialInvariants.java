package com.foundgine.samples.supplychain.advanced.scenarios;

import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;

import java.time.LocalDate;

/** Executable adversarial invariants matching the Advanced C# sample. */
public final class ScenariosAdversarialInvariants {
    public static void assertAdversarialInvariants(SupplyChainData d, Authorization.Context auth) {
        if (d == null || auth == null) throw new NullPointerException();

        boolean restrictedWarehousePresent =
                d.inventory.stream()
                        .anyMatch(
                                i -> i.warehouseId() == 3 && !auth.allowedWarehouses().contains(3));
        if (restrictedWarehousePresent) {
            // Deliberately informational: presence in the fixture is expected;
            // authorization must be what excludes it from a caller's scope.
        }

        boolean cycle =
                Scenarios.recursiveSupplierRisk(d, 1, 16, auth).stream()
                        .anyMatch(Scenarios.SupplierRisk::cycleDetected);
        if (!cycle) throw new IllegalStateException("Expected BOM cycle to be detected.");

        boolean expired =
                d.certifications.stream()
                        .filter(c -> c.validTo().isBefore(LocalDate.of(2026, 8, 26)))
                        .anyMatch(c -> c.supplierId() == 3);
        if (!expired)
            throw new IllegalStateException(
                    "Expected expired certification fixture for supplier three.");

        if (auth.allowedWarehouses().contains(3)) {
            throw new IllegalStateException(
                    "Adversarial authorization context accidentally exposes tenant-b warehouse.");
        }
    }

    private ScenariosAdversarialInvariants() {}
}
