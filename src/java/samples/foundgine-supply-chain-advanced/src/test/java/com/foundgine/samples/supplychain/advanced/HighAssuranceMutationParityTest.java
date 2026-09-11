package com.foundgine.samples.supplychain.advanced;

import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.mutation.CancelOrderService;
import com.foundgine.samples.supplychain.advanced.mutation.PlaceOrderService;
import org.junit.jupiter.api.Test;

import java.math.BigDecimal;
import java.util.List;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.*;

/** High-assurance mutation invariants mirrored from the C# Advanced/E2E suites. */
class HighAssuranceMutationParityTest {
    private static Authorization.Context customer(String tenant) {
        return new Authorization.Context(tenant, Set.of(1, 2), Authorization.Role.CUSTOMER, false);
    }

    @Test
    void duplicate_lines_are_collapsed_into_one_logical_order_item() {
        var data = SupplyChainData.seed();
        var result = new PlaceOrderService(data).placeOrder(
                "alice", customer("tenant-a"), 1,
                List.of(new PlaceOrderService.OrderLine(4, 1), new PlaceOrderService.OrderLine(4, 2)),
                "duplicate-lines");

        assertEquals(new BigDecimal("102.00"), result.total());
        assertEquals(1, data.orderItems.stream().filter(x -> x.orderId() == result.orderId()).count());
        assertEquals(3, data.orderItems.stream().filter(x -> x.orderId() == result.orderId()).findFirst().orElseThrow().quantity());
    }

    @Test
    void unauthorized_cancel_does_not_change_order_or_inventory() {
        var data = SupplyChainData.seed();
        var placeAuth = customer("tenant-a");
        var order = new PlaceOrderService(data).placeOrder(
                "alice", placeAuth, 1, List.of(new PlaceOrderService.OrderLine(4, 2)), "cancel-boundary-place");
        var before = data.inventory.stream().filter(x -> x.id() == 900).findFirst().orElseThrow().onHand();
        var denied = new Authorization.Context("tenant-a", Set.of(1, 2), Authorization.Role.CUSTOMER, true);

        assertThrows(SecurityException.class, () -> new CancelOrderService(data)
                .cancelOrder("alice", denied, order.orderId(), "cancel-boundary"));

        assertEquals(before, data.inventory.stream().filter(x -> x.id() == 900).findFirst().orElseThrow().onHand());
        assertEquals("Pending", data.orders.stream().filter(x -> x.id() == order.orderId()).findFirst().orElseThrow().status());
    }

    @Test
    void cancelled_order_cannot_be_cancelled_again_with_a_new_key() {
        var data = SupplyChainData.seed();
        var auth = customer("tenant-a");
        var place = new PlaceOrderService(data);
        var order = place.placeOrder("alice", auth, 1,
                List.of(new PlaceOrderService.OrderLine(4, 1)), "already-cancelled-place");
        var cancel = new CancelOrderService(data);
        cancel.cancelOrder("alice", auth, order.orderId(), "first-cancel");

        assertThrows(IllegalStateException.class, () -> cancel.cancelOrder(
                "alice", auth, order.orderId(), "second-cancel"));
    }

    @Test
    void cancellation_restores_the_exact_allocated_lot() {
        var data = SupplyChainData.seed();
        var auth = customer("tenant-a");
        var place = new PlaceOrderService(data);
        var before900 = data.inventory.stream().filter(x -> x.id() == 900).findFirst().orElseThrow().onHand();
        var before901 = data.inventory.stream().filter(x -> x.id() == 901).findFirst().orElseThrow().onHand();
        var order = place.placeOrder("alice", auth, 1,
                List.of(new PlaceOrderService.OrderLine(4, 2)), "exact-lot-place");

        var allocatedLot = data.orderAllocations.stream()
                .filter(x -> data.orderItems.stream().anyMatch(i -> i.id() == x.orderItemId() && i.orderId() == order.orderId()))
                .findFirst().orElseThrow().lotId();
        assertEquals(900, allocatedLot);

        new CancelOrderService(data).cancelOrder("alice", auth, order.orderId(), "exact-lot-cancel");
        assertEquals(before900, data.inventory.stream().filter(x -> x.id() == 900).findFirst().orElseThrow().onHand());
        assertEquals(before901, data.inventory.stream().filter(x -> x.id() == 901).findFirst().orElseThrow().onHand());
    }
}
