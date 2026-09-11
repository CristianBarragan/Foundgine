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
    @Test void placeOrderUsesServerSideProductPriceNotSafetyStock() {
        var data = SupplyChainData.seed();
        var auth = new Authorization.Context("tenant-a", java.util.Set.of(1,2), Authorization.Role.CUSTOMER, false);
        var result = new PlaceOrderService(data).placeOrder("alice", auth, 1,
                java.util.List.of(new PlaceOrderService.OrderLine(4, 2)), "price-1");
        assertEquals(new java.math.BigDecimal("68.00"), result.total());
        var item = data.orderItems.stream().filter(x -> x.orderId() == result.orderId()).findFirst().orElseThrow();
        assertEquals(new java.math.BigDecimal("34.00"), item.unitPrice());
    }

    @Test void mcpMutationFacadeRoutesThroughRuntimeBoundary() throws Exception {
        var data = SupplyChainData.seed();
        var auth = new Authorization.Context("tenant-a", java.util.Set.of(1,2), Authorization.Role.CUSTOMER, false);
        var facade = new com.foundgine.samples.supplychain.advanced.mutation.AdvancedMcpFacade(data, auth);
        var response = facade.mutationTools().foundgineMutation(
                "{\"tool\":\"place_order\",\"actor\":\"alice\",\"customerId\":1,\"productId\":4,\"quantity\":2,\"idempotencyKey\":\"mcp-place-1\"}")
                .toCompletableFuture().join();
        var json = new com.fasterxml.jackson.databind.ObjectMapper().writeValueAsString(response);
        assertTrue(json.contains("planFingerprint"));
        assertTrue(json.contains("resultFingerprint"));
        var capabilities = facade.queryTools().foundgineQuery(
                "{\"tool\":\"capabilities\"}").toCompletableFuture().join();
        assertTrue(capabilities.contains("place_order"));
        assertTrue(capabilities.contains("cancel_order"));
    }

    @Test void reusing_place_order_idempotency_key_for_different_request_is_rejected() {
        var data = SupplyChainData.seed();
        var auth = new Authorization.Context("tenant-a", java.util.Set.of(1,2), Authorization.Role.CUSTOMER, false);
        var service = new PlaceOrderService(data);
        service.placeOrder("alice", auth, 1, java.util.List.of(new PlaceOrderService.OrderLine(4, 1)), "bound-place");
        var ex = assertThrows(IllegalStateException.class, () -> service.placeOrder("alice", auth, 1,
                java.util.List.of(new PlaceOrderService.OrderLine(4, 2)), "bound-place"));
        assertTrue(ex.getMessage().contains("bound to a different"));
    }

    @Test void reusing_cancel_idempotency_key_for_different_order_is_rejected() {
        var data = SupplyChainData.seed();
        var auth = new Authorization.Context("tenant-a", java.util.Set.of(1,2), Authorization.Role.CUSTOMER, false);
        var place = new PlaceOrderService(data);
        var first = place.placeOrder("alice", auth, 1, java.util.List.of(new PlaceOrderService.OrderLine(4, 1)), "place-a");
        var second = place.placeOrder("alice", auth, 1, java.util.List.of(new PlaceOrderService.OrderLine(4, 1)), "place-b");
        var cancel = new CancelOrderService(data);
        cancel.cancelOrder("alice", auth, first.orderId(), "bound-cancel");
        var ex = assertThrows(IllegalStateException.class, () -> cancel.cancelOrder("alice", auth, second.orderId(), "bound-cancel"));
        assertTrue(ex.getMessage().contains("bound to a different"));
    }

    @Test void concurrent_same_place_order_key_is_applied_once() throws Exception {
        var data = SupplyChainData.seed();
        var auth = new Authorization.Context("tenant-a", java.util.Set.of(1,2), Authorization.Role.CUSTOMER, false);
        var service = new PlaceOrderService(data);
        var task = java.util.concurrent.Executors.newFixedThreadPool(4);
        try {
            var futures = java.util.stream.IntStream.range(0, 4).mapToObj(i -> task.submit(() ->
                    service.placeOrder("alice", auth, 1, java.util.List.of(new PlaceOrderService.OrderLine(4, 2)), "concurrent-place"))).toList();
            var results = new java.util.ArrayList<PlaceOrderService.Result>();
            for (var f : futures) results.add(f.get());
            assertEquals(1, results.stream().map(PlaceOrderService.Result::orderId).distinct().count());
            assertEquals(1, data.orders.size());
            assertEquals(1, data.idempotency.size());
        } finally { task.shutdownNow(); }
    }

    @Test void replay_still_requires_current_authorization() {
        var data = SupplyChainData.seed();
        var allowed = new Authorization.Context("tenant-a", java.util.Set.of(1,2), Authorization.Role.CUSTOMER, false);
        var service = new PlaceOrderService(data);
        service.placeOrder("alice", allowed, 1, java.util.List.of(new PlaceOrderService.OrderLine(4, 1)), "auth-replay");
        var denied = new Authorization.Context("tenant-a", java.util.Set.of(1,2), Authorization.Role.CUSTOMER, true);
        assertThrows(SecurityException.class, () -> service.placeOrder("alice", denied, 1,
                java.util.List.of(new PlaceOrderService.OrderLine(4, 1)), "auth-replay"));
    }

}
