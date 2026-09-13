package com.foundgine.samples.supplychain.advanced;

import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import org.junit.jupiter.api.Test;

import java.time.LocalDate;
import java.util.HashSet;
import java.util.stream.Collectors;

import static org.junit.jupiter.api.Assertions.*;

class SupplyChainDataFixtureParityTest {
    @Test
    void seedContainsABomComponentCycle() {
        var data = SupplyChainData.seed();
        var byParent = data.components.stream()
                .collect(Collectors.groupingBy(x -> x.parentProductId()));

        boolean hasCycle = data.products.stream().anyMatch(product -> reachesStart(
                product.id(), product.id(), byParent, new HashSet<>()));

        assertTrue(hasCycle);
    }

    private static boolean reachesStart(int start, int current,
                                        java.util.Map<Integer, java.util.List<com.foundgine.samples.supplychain.advanced.domain.Domain.ProductComponent>> byParent,
                                        java.util.Set<Integer> visited) {
        for (var component : byParent.getOrDefault(current, java.util.List.of())) {
            int child = component.componentProductId();
            if (child == start) return true;
            if (visited.add(child) && reachesStart(start, child, byParent, visited)) return true;
        }
        return false;
    }

    @Test
    void seedContainsMultipleTenants() {
        var data = SupplyChainData.seed();
        assertTrue(data.warehouses.stream().map(x -> x.tenantId()).distinct().count() > 1);
    }

    @Test
    void seedContainsExpiredSupplierCertification() {
        var data = SupplyChainData.seed();
        var referenceDate = LocalDate.of(2026, 8, 27);
        assertTrue(data.certifications.stream().anyMatch(x -> x.validTo().isBefore(referenceDate)));
    }

    @Test
    void seedContainsCancelledPurchaseOrder() {
        var data = SupplyChainData.seed();
        assertTrue(data.purchaseOrders.stream()
                .anyMatch(x -> x.status() == com.foundgine.samples.supplychain.advanced.domain.Domain.PurchaseOrderStatus.CANCELLED));
    }

    @Test
    void seedContainsPartiallyReceivedOrDelayedShipment() {
        var data = SupplyChainData.seed();
        assertTrue(data.shipments.stream().anyMatch(x ->
                x.status() == com.foundgine.samples.supplychain.advanced.domain.Domain.ShipmentStatus.DELAYED
                        || x.status() == com.foundgine.samples.supplychain.advanced.domain.Domain.ShipmentStatus.PARTIALLY_RECEIVED));
    }

    @Test
    void seedContainsQuarantinedInventory() {
        var data = SupplyChainData.seed();
        assertTrue(data.inventory.stream().anyMatch(x -> x.quarantined().signum() > 0));
    }

    @Test
    void seedContainsReservedInventory() {
        var data = SupplyChainData.seed();
        assertTrue(data.inventory.stream().anyMatch(x -> x.reserved().signum() > 0));
    }

    @Test
    void everyPurchaseOrderLineReferencesAnExistingPurchaseOrder() {
        var data = SupplyChainData.seed();
        var ids = data.purchaseOrders.stream().map(x -> x.id()).collect(Collectors.toSet());
        assertTrue(data.purchaseOrderLines.stream().allMatch(x -> ids.contains(x.purchaseOrderId())));
    }

    @Test
    void everyShipmentReferencesAnExistingPurchaseOrder() {
        var data = SupplyChainData.seed();
        var ids = data.purchaseOrders.stream().map(x -> x.id()).collect(Collectors.toSet());
        assertTrue(data.shipments.stream().allMatch(x -> ids.contains(x.purchaseOrderId())));
    }

    @Test
    void everyCustomerOrderLineReferencesAnExistingCustomerOrder() {
        var data = SupplyChainData.seed();
        var ids = data.customerOrders.stream().map(x -> x.id()).collect(Collectors.toSet());
        assertTrue(data.customerOrderLines.stream().allMatch(x -> ids.contains(x.customerOrderId())));
    }
}
