package com.foundgine.samples.supplychain.advanced;

import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.domain.Domain.InventoryLot;
import com.foundgine.samples.supplychain.advanced.mutation.CancelOrderService;
import com.foundgine.samples.supplychain.advanced.mutation.PlaceOrderService;
import com.foundgine.samples.supplychain.advanced.mutation.AdvancedMutationPipeline;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

class AdvancedMutationTest {
    @Test void cancelRestoresAllocatedInventoryAndIsIdempotent() {
        var data = SupplyChainData.seed();
        var auth = new Authorization.Context("tenant-a", java.util.Set.of(1,2), Authorization.Role.CUSTOMER, false);
        var place = new PlaceOrderService(data);
        var before = data.inventory.stream().filter(x -> x.id() == 900).findFirst().orElseThrow().onHand();
        var created = place.placeOrder("alice", auth, 1, java.util.List.of(new PlaceOrderService.OrderLine(4, 2)), "place-1");
        var afterPlace = data.inventory.stream().filter(x -> x.id() == 900).findFirst().orElseThrow().onHand();
        assertEquals(before.subtract(java.math.BigDecimal.valueOf(2)), afterPlace);

        var cancel = new CancelOrderService(data);
        var result = cancel.cancelOrder("alice", auth, created.orderId(), "cancel-1");
        assertFalse(result.replay());
        assertEquals(java.math.BigDecimal.valueOf(2), result.restoredQuantity());
        assertEquals(before, data.inventory.stream().filter(x -> x.id() == 900).findFirst().orElseThrow().onHand());
        assertEquals("Cancelled", data.orders.stream().filter(x -> x.id() == created.orderId()).findFirst().orElseThrow().status());

        var replay = cancel.cancelOrder("alice", auth, created.orderId(), "cancel-1");
        assertTrue(replay.replay());
        assertEquals(result.evidence(), replay.evidence());
        assertEquals(before, data.inventory.stream().filter(x -> x.id() == 900).findFirst().orElseThrow().onHand());
    }

    @Test void runtimePipelineProducesDeterministicPlanAndIdempotentReplay() {
        var data = SupplyChainData.seed();
        var auth = new Authorization.Context("tenant-a", java.util.Set.of(1,2), Authorization.Role.SUPPLY_CHAIN_MANAGER, false);
        var pipeline = new AdvancedMutationPipeline(data, auth);
        var first = pipeline.placeOrder("alice", 1, 4, 2, "runtime-place-1").toCompletableFuture().join();
        var replay = pipeline.placeOrder("alice", 1, 4, 2, "runtime-place-1").toCompletableFuture().join();
        assertEquals(first.planFingerprint(), replay.planFingerprint());
        assertEquals(first.resultFingerprint(), replay.resultFingerprint());
        var orderId = ((Number) first.result().results().getFirst().returnedValues().values().iterator().next()).intValue();
        var cancelled = pipeline.cancelOrder("alice", orderId, "runtime-cancel-1").toCompletableFuture().join();
        var cancelReplay = pipeline.cancelOrder("alice", orderId, "runtime-cancel-1").toCompletableFuture().join();
        assertEquals(cancelled.planFingerprint(), cancelReplay.planFingerprint());
        assertEquals(cancelled.resultFingerprint(), cancelReplay.resultFingerprint());
    }

    @Test void customerCannotCancelAnotherTenantOrder() {
        var data = SupplyChainData.seed();
        var auth = new Authorization.Context("tenant-b", java.util.Set.of(3), Authorization.Role.CUSTOMER, false);
        var placeAuth = new Authorization.Context("tenant-a", java.util.Set.of(1,2), Authorization.Role.CUSTOMER, false);
        var order = new PlaceOrderService(data).placeOrder("alice", placeAuth, 1,
                java.util.List.of(new PlaceOrderService.OrderLine(4, 1)), "place-cross");
        assertThrows(SecurityException.class, () -> new CancelOrderService(data).cancelOrder("alice", auth, order.orderId(), "cancel-cross"));
    }
}
