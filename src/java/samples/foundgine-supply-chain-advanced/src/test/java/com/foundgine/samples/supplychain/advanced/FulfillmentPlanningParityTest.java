package com.foundgine.samples.supplychain.advanced;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.domain.Domain.*;
import com.foundgine.samples.supplychain.advanced.scenarios.Scenarios;

import org.junit.jupiter.api.Test;

import java.math.BigDecimal;
import java.time.LocalDate;
import java.util.Set;

class FulfillmentPlanningParityTest {
    private static Authorization.Context tenantA() {
        return new Authorization.Context(
                "tenant-a", Set.of(1, 2), Authorization.Role.SUPPLY_CHAIN_MANAGER, false);
    }

    @Test
    void excludesReservedAndQuarantinedInventory() {
        var data = SupplyChainData.seed();
        var risks = Scenarios.fulfillment(data, LocalDate.of(2026, 8, 27), tenantA());

        var inventoryForProduct4 =
                data.inventory.stream()
                        .filter(
                                x ->
                                        x.productId() == 4
                                                && tenantA()
                                                        .allowedWarehouses()
                                                        .contains(x.warehouseId()))
                        .map(
                                x ->
                                        x.onHand()
                                                .subtract(x.reserved())
                                                .subtract(x.quarantined())
                                                .max(BigDecimal.ZERO))
                        .reduce(BigDecimal.ZERO, BigDecimal::add);

        assertEquals(new BigDecimal("40"), inventoryForProduct4);
        assertTrue(risks.stream().noneMatch(x -> x.productId() == 4));
    }

    @Test
    void doesNotUseCancelledPurchaseOrders() {
        var data = SupplyChainData.seed();
        data.customerOrderLines.add(new CustomerOrderLine(9000, 700, 4, new BigDecimal("5000")));

        var risks = Scenarios.fulfillment(data, LocalDate.of(2026, 8, 27), tenantA());
        var risk = risks.stream().filter(x -> x.productId() == 4).findFirst().orElseThrow();

        assertEquals(new BigDecimal("3960"), risk.shortage());
    }

    @Test
    void excludesInventoryInUnauthorizedWarehouse() {
        var data = SupplyChainData.seed();
        data.customerOrderLines.add(new CustomerOrderLine(9001, 700, 4, new BigDecimal("4500")));

        var risks = Scenarios.fulfillment(data, LocalDate.of(2026, 8, 27), tenantA());
        var risk = risks.stream().filter(x -> x.productId() == 4).findFirst().orElseThrow();

        assertEquals(new BigDecimal("3460"), risk.shortage());
    }

    @Test
    void resultsAreStablyOrderedByShortageThenProductId() {
        var data = SupplyChainData.seed();
        var risks = Scenarios.fulfillment(data, LocalDate.of(2026, 8, 27), tenantA());

        for (int i = 1; i < risks.size(); i++) {
            var previous = risks.get(i - 1);
            var current = risks.get(i);
            int shortageOrder = current.shortage().compareTo(previous.shortage());
            assertTrue(shortageOrder <= 0);
            if (shortageOrder == 0) {
                assertTrue(previous.productId() <= current.productId());
            }
        }
    }
}
