package com.foundgine.samples.supplychain.advanced;

import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.scenarios.Scenarios;

import org.junit.jupiter.api.Test;

import java.util.Set;

/**
 * Port of C# {@code Foundgine.SupplyChain.Advanced.Tests.RecursiveSupplierRiskTests}.
 *
 * <p><b>Porting decisions / prior-session context:</b> the parity audit found that Java's {@code
 * Scenarios.recursiveSupplierRisk(SupplyChainData, int, int)} had no {@code
 * AuthorizationContext}-equivalent parameter and performed no tenant filtering at all, unlike C#'s
 * {@code SupplyChainScenarios.RecursiveSupplierRisk(SupplyChainData, ProductId,
 * AuthorizationContext)}, which filters supplier exposures by {@code auth.TenantId} inside the
 * traversal. This was a real functional gap, not just a missing test, so it has been fixed in
 * {@code Scenarios.java} first (added an {@code Authorization.Context} parameter and a {@code
 * supplierId} field on {@code SupplierRisk}); this test now closes the coverage gap against the
 * fixed code.
 * <li>C#'s {@code AuthorizationContext} has explicit {@code CanReadSupplierRisk}/ {@code
 *     CanWritePurchasing} booleans with no Java equivalent; the existing Java {@code
 *     Authorization.Context} instead derives capability from {@code Role}, so {@code
 *     SUPPLY_CHAIN_MANAGER} (for which {@code Authorization.canReadSupplierRisk} is true) is used
 *     as the closest matching context, consistent with how the rest of this sample already
 *     constructs contexts.
 * <li>C#'s {@code SupplyChainExecutionLimits.RecursiveBomMaxDepth} constant (5) has no direct Java
 *     equivalent -- Java already models this as an explicit {@code maxDepth} argument rather than a
 *     shared constant -- so {@code 5} is passed directly to match the C# test's depth-range
 *     assertion.
 */
class RecursiveSupplierRiskParityTest {

    private static Authorization.Context tenantAAuth() {
        return new Authorization.Context(
                "tenant-a", Set.of(1, 2), Authorization.Role.SUPPLY_CHAIN_MANAGER, false);
    }

    @Test
    void detectsTheSeedBomCycleAndTerminatesWithinTheConfiguredDepth() {
        var data = SupplyChainData.seed();

        var result = Scenarios.recursiveSupplierRisk(data, 1, 5, tenantAAuth());

        assertTrue(result.stream().anyMatch(Scenarios.SupplierRisk::cycleDetected));
        assertTrue(result.stream().allMatch(x -> x.depth() >= 0 && x.depth() <= 5));
    }

    @Test
    void doesNotReturnSupplierExposuresFromAnotherTenant() {
        var data = SupplyChainData.seed();

        var result = Scenarios.recursiveSupplierRisk(data, 1, 5, tenantAAuth());

        assertTrue(result.stream().noneMatch(x -> Integer.valueOf(3).equals(x.supplierId())));
    }
}
