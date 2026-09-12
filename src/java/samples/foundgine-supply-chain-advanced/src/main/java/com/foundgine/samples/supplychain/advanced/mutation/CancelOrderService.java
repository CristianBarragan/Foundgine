package com.foundgine.samples.supplychain.advanced.mutation;

import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.domain.Domain.*;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;

import java.math.BigDecimal;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.LocalDate;
import java.util.*;

/** High-assurance cancellation boundary. Restores every allocation atomically and is idempotent. */
public final class CancelOrderService {
    public record Result(int orderId, boolean replay, BigDecimal restoredQuantity,
                         String planFingerprint, String evidence) {}

    private final SupplyChainData data;
    private final Map<String, Object> locks = new java.util.concurrent.ConcurrentHashMap<>();

    public CancelOrderService(SupplyChainData data) { this.data = Objects.requireNonNull(data); }

    public Result cancelOrder(String actor, Authorization.Context auth, int orderId, String key) {
        Objects.requireNonNull(actor);
        Objects.requireNonNull(auth);
        if (orderId <= 0) throw new IllegalArgumentException("Order id must be positive.");
        if (key == null || key.isBlank()) throw new IllegalArgumentException("Idempotency key is required.");
        if (auth.readOnly() || (auth.role() != Authorization.Role.CUSTOMER && auth.role() != Authorization.Role.SUPPLY_CHAIN_MANAGER))
            throw new SecurityException("Caller is not authorized to cancel orders.");

        var requestFingerprint = requestFingerprint(actor, orderId);
        synchronized (lockFor(key)) {
            var prior = data.cancellationIdempotency.stream().filter(x -> x.key().equals(key)).findFirst().orElse(null);
            if (prior != null) {
                if (!prior.requestFingerprint().equals(requestFingerprint)) throw new IllegalStateException("Idempotency key is bound to a different request.");
                return new Result(prior.orderId(), true, prior.restoredQuantity(), planFingerprint(),
                        evidence(actor, prior.orderId(), key, prior.restoredQuantity()));
            }

            var order = data.orders.stream().filter(x -> x.id() == orderId).findFirst()
                    .orElseThrow(() -> new IllegalArgumentException("Order not found."));
            var customer = data.customers.stream().filter(x -> x.id() == order.customerId()).findFirst()
                    .orElseThrow(() -> new IllegalStateException("Order customer not found."));
            if (!customer.tenantId().equals(auth.tenantId())) throw new SecurityException("Order belongs to another tenant.");
            if (auth.role() == Authorization.Role.CUSTOMER && !actorCustomer(actor, customer.id()))
                throw new SecurityException("Customer ownership check failed.");
            if ("Cancelled".equalsIgnoreCase(order.status()))
                throw new IllegalStateException("Order is already cancelled.");
            if (!"Pending".equalsIgnoreCase(order.status()))
                throw new IllegalStateException("Only pending orders can be cancelled by this boundary.");

            var orders = new ArrayList<>(data.orders);
            var inventory = new ArrayList<>(data.inventory);
            var idem = new ArrayList<>(data.cancellationIdempotency);
            try {
                BigDecimal restored = BigDecimal.ZERO;
                for (var allocation : data.orderAllocations.stream()
                        .filter(a -> data.orderItems.stream().anyMatch(i -> i.id() == a.orderItemId() && i.orderId() == orderId))
                        .toList()) {
                    var candidate = data.inventory.stream().filter(x -> x.id() == allocation.lotId())
                            .filter(x -> auth.allowedWarehouses().contains(x.warehouseId()))
                            .findFirst()
                            .orElseThrow(() -> new IllegalStateException("Allocated inventory lot cannot be resolved."));
                    data.inventory.set(data.inventory.indexOf(candidate),
                            new InventoryLot(candidate.id(), candidate.warehouseId(), candidate.productId(),
                                    candidate.onHand().add(BigDecimal.valueOf(allocation.quantity())), candidate.reserved(),
                                    candidate.quarantined(), candidate.receivedOn()));
                    restored = restored.add(BigDecimal.valueOf(allocation.quantity()));
                }
                data.orders.set(data.orders.indexOf(order),
                        new Order(order.id(), order.customerId(), "Cancelled", order.totalAmount(), order.placedOn()));
                data.cancellationIdempotency.add(new CancellationIdempotencyRecord(key, actor, orderId, requestFingerprint, restored, LocalDate.now()));
                return new Result(orderId, false, restored, planFingerprint(), evidence(actor, orderId, key, restored));
            } catch (RuntimeException ex) {
                data.orders.clear(); data.orders.addAll(orders);
                data.inventory.clear(); data.inventory.addAll(inventory);
                data.cancellationIdempotency.clear(); data.cancellationIdempotency.addAll(idem);
                throw ex;
            }
        }
    }

    private boolean actorCustomer(String actor, int id) {
        return actor.equalsIgnoreCase("customer" + id) || actor.equalsIgnoreCase("customer-" + id)
                || (actor.equalsIgnoreCase("alice") && id == 1) || (actor.equalsIgnoreCase("bob") && id == 2);
    }
    private Object lockFor(String key) { return locks.computeIfAbsent(key, k -> new Object()); }
    private static String requestFingerprint(String actor, int orderId) { return sha256("cancel_order|" + actor + "|" + orderId); }
    private String planFingerprint() {
        return sha256("cancel_order|" + SupplyChainSemanticModel.MODEL.contractFingerprint() + "|Order|OrderItem|OrderAllocation|InventoryLot").substring(0, 24);
    }
    private static String evidence(String actor, int order, String key, BigDecimal restored) {
        return sha256("cancel_order|" + actor + "|" + order + "|" + key + "|" + restored.toPlainString());
    }
    private static String sha256(String value) {
        try {
            var digest = MessageDigest.getInstance("SHA-256");
            var bytes = digest.digest(value.getBytes(StandardCharsets.UTF_8));
            var result = new StringBuilder();
            for (byte b : bytes) result.append(String.format("%02x", b));
            return result.toString();
        } catch (Exception e) { throw new IllegalStateException(e); }
    }
}
