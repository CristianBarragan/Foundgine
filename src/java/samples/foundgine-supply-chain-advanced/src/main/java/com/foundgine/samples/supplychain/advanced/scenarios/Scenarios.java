package com.foundgine.samples.supplychain.advanced.scenarios;

import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.domain.Domain.*;

import java.math.BigDecimal;
import java.time.LocalDate;
import java.util.*;

public final class Scenarios {
    public record SupplierRisk(
            int productId,
            Integer supplierId,
            int depth,
            boolean cycleDetected,
            List<Integer> path) {}

    public record FulfillmentRisk(
            int productId,
            String sku,
            BigDecimal demand,
            BigDecimal available,
            BigDecimal inbound,
            BigDecimal shortage,
            Set<Integer> suppliers) {}

    /**
     * Port of C# SupplyChainScenarios.RecursiveSupplierRisk.
     *
     * <p>The authorization context is deliberately part of the scenario API: supplier exposures are
     * filtered by tenant while traversing, rather than being filtered after an already-authorized
     * result has been constructed.
     */
    public static List<SupplierRisk> recursiveSupplierRisk(
            SupplyChainData d, int root, int maxDepth, Authorization.Context auth) {
        Objects.requireNonNull(d, "data");
        Objects.requireNonNull(auth, "auth");

        Map<Integer, List<Integer>> graph = new HashMap<>();
        for (var component : d.components) {
            graph.computeIfAbsent(component.parentProductId(), ignored -> new ArrayList<>())
                    .add(component.componentProductId());
        }

        List<SupplierRisk> result = new ArrayList<>();
        Set<Integer> visited = new HashSet<>();
        walk(d, graph, root, 0, maxDepth, new HashSet<>(), visited, result, auth);
        return List.copyOf(result);
    }

    private static void walk(
            SupplyChainData d,
            Map<Integer, List<Integer>> graph,
            int product,
            int depth,
            int maxDepth,
            Set<Integer> path,
            Set<Integer> visited,
            List<SupplierRisk> result,
            Authorization.Context auth) {
        if (depth > maxDepth) {
            return;
        }

        if (!path.add(product)) {
            result.add(new SupplierRisk(product, null, depth, true, List.copyOf(path)));
            return;
        }

        // Match the C# scenario: once a product has been traversed outside the
        // current path, do not traverse it again. Path cycles are still detected.
        if (!visited.add(product)) {
            path.remove(product);
            return;
        }

        for (int child : graph.getOrDefault(product, List.of())) {
            List<Integer> supplierIds =
                    d.purchaseOrderLines.stream()
                            .filter(line -> line.productId() == child)
                            .map(
                                    line ->
                                            d.purchaseOrders.stream()
                                                    .filter(po -> po.id() == line.purchaseOrderId())
                                                    .map(PurchaseOrder::supplierId)
                                                    .findFirst()
                                                    .orElse(null))
                            .filter(Objects::nonNull)
                            .distinct()
                            .toList();

            for (int supplierId : supplierIds) {
                boolean sameTenant =
                        d.suppliers.stream()
                                .filter(supplier -> supplier.id() == supplierId)
                                .findFirst()
                                .map(supplier -> supplier.tenantId().equals(auth.tenantId()))
                                .orElse(false);
                if (sameTenant) {
                    result.add(
                            new SupplierRisk(
                                    child, supplierId, depth + 1, false, List.copyOf(path)));
                }
            }

            walk(d, graph, child, depth + 1, maxDepth, path, visited, result, auth);
        }

        path.remove(product);
    }

    /**
     * Port of C# SupplyChainScenarios.FulfillmentPlanning.
     *
     * <p>Demand is restricted to open customer orders. Inventory is reduced by reserved/quarantined
     * quantities and warehouse authorization. Inbound supply only comes from open or
     * partially-received purchase orders in authorized warehouses with an
     * in-transit/delayed/partially-received shipment arriving within 14 days of {@code asOf}.
     * Supplier identities are tenant-scoped, and results are stably ordered by shortage then
     * product.
     */
    public static List<FulfillmentRisk> fulfillment(
            SupplyChainData d, LocalDate asOf, Authorization.Context auth) {
        Objects.requireNonNull(d, "data");
        Objects.requireNonNull(asOf, "asOf");
        Objects.requireNonNull(auth, "auth");

        Map<Integer, BigDecimal> demand = new LinkedHashMap<>();
        for (var line : d.customerOrderLines) {
            var order =
                    d.customerOrders.stream()
                            .filter(candidate -> candidate.id() == line.customerOrderId())
                            .findFirst()
                            .orElse(null);
            if (order != null && "Open".equals(order.status())) {
                demand.merge(line.productId(), line.quantity(), BigDecimal::add);
            }
        }

        List<FulfillmentRisk> output = new ArrayList<>();
        LocalDate inboundCutoff = asOf.plusDays(14);

        for (var entry : demand.entrySet()) {
            int productId = entry.getKey();
            BigDecimal qty = entry.getValue();

            BigDecimal available =
                    d.inventory.stream()
                            .filter(
                                    i ->
                                            i.productId() == productId
                                                    && auth.allowedWarehouses()
                                                            .contains(i.warehouseId()))
                            .map(
                                    i ->
                                            i.onHand()
                                                    .subtract(i.reserved())
                                                    .subtract(i.quarantined())
                                                    .max(BigDecimal.ZERO))
                            .reduce(BigDecimal.ZERO, BigDecimal::add);

            BigDecimal inbound = BigDecimal.ZERO;
            for (var line : d.purchaseOrderLines) {
                if (line.productId() != productId) {
                    continue;
                }
                var purchaseOrder =
                        d.purchaseOrders.stream()
                                .filter(po -> po.id() == line.purchaseOrderId())
                                .findFirst()
                                .orElse(null);
                if (purchaseOrder == null
                        || !isOpenOrPartiallyReceived(purchaseOrder.status())
                        || !auth.allowedWarehouses().contains(purchaseOrder.warehouseId())) {
                    continue;
                }

                for (var shipment : d.shipments) {
                    if (shipment.purchaseOrderId() == purchaseOrder.id()
                            && isInboundShipment(shipment.status())
                            && !shipment.expectedArrival().isAfter(inboundCutoff)) {
                        inbound = inbound.add(shipment.quantity());
                    }
                }
            }

            List<Integer> suppliers =
                    d.purchaseOrderLines.stream()
                            .filter(line -> line.productId() == productId)
                            .map(
                                    line ->
                                            d.purchaseOrders.stream()
                                                    .filter(po -> po.id() == line.purchaseOrderId())
                                                    .map(PurchaseOrder::supplierId)
                                                    .findFirst()
                                                    .orElse(null))
                            .filter(Objects::nonNull)
                            .distinct()
                            .filter(
                                    supplierId ->
                                            d.suppliers.stream()
                                                    .anyMatch(
                                                            supplier ->
                                                                    supplier.id() == supplierId
                                                                            && supplier.tenantId()
                                                                                    .equals(
                                                                                            auth
                                                                                                    .tenantId())))
                            .toList();

            BigDecimal shortage = qty.subtract(available).subtract(inbound).max(BigDecimal.ZERO);
            if (shortage.signum() <= 0) {
                continue;
            }

            var product =
                    d.products.stream()
                            .filter(candidate -> candidate.id() == productId)
                            .findFirst()
                            .orElseThrow();
            output.add(
                    new FulfillmentRisk(
                            productId,
                            product.sku(),
                            qty,
                            available,
                            inbound,
                            shortage,
                            new LinkedHashSet<>(suppliers)));
        }

        output.sort(
                Comparator.comparing(FulfillmentRisk::shortage, Comparator.reverseOrder())
                        .thenComparingInt(FulfillmentRisk::productId));
        return List.copyOf(output.stream().limit(20).toList());
    }

    /** Compatibility overload retained for callers that do not need a custom as-of date. */
    public static List<FulfillmentRisk> fulfillment(SupplyChainData d, Authorization.Context auth) {
        return fulfillment(d, LocalDate.of(2026, 8, 27), auth);
    }

    private static boolean isOpenOrPartiallyReceived(PurchaseOrderStatus status) {
        return status == PurchaseOrderStatus.OPEN
                || status == PurchaseOrderStatus.PARTIALLY_RECEIVED;
    }

    private static boolean isInboundShipment(ShipmentStatus status) {
        return status == ShipmentStatus.IN_TRANSIT
                || status == ShipmentStatus.DELAYED
                || status == ShipmentStatus.PARTIALLY_RECEIVED;
    }

    private Scenarios() {}
}
