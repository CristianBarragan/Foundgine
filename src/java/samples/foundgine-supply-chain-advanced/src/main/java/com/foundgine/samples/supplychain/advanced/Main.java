package com.foundgine.samples.supplychain.advanced;

import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.authorization.claims.Claims;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.scenarios.Scenarios;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;

import java.time.Instant;
import java.util.*;

public final class Main {
    public static void main(String[] args) {
        var data = SupplyChainData.seed();
        var identity =
                new Authorization.Context(
                        "tenant-a", Set.of(1, 2), Authorization.Role.SUPPLY_CHAIN_MANAGER, false);
        var claims =
                Claims.validate(
                        Map.of("warehouse", "1", "scope", "read-only", "max_rows", "100"),
                        Instant.now());
        var effective = Authorization.applyClaims(identity, claims);
        var cycles = Scenarios.recursiveSupplierRisk(data, 1, 16, effective);
        System.out.println("Foundgine Advanced Supply Chain Java sample");
        System.out.println(
                "accepted claims="
                        + claims.accepted().keySet()
                        + ", effective warehouses="
                        + effective.allowedWarehouses());
        System.out.println(
                "BOM cycles detected="
                        + cycles.stream().filter(Scenarios.SupplierRisk::cycleDetected).count());
        System.out.println("fulfillment rows=" + Scenarios.fulfillment(data, effective).size());
        var orderAuth =
                new Authorization.Context(
                        "tenant-a", Set.of(1, 2), Authorization.Role.SUPPLY_CHAIN_MANAGER, false);
        var pipeline =
                new com.foundgine.samples.supplychain.advanced.mutation.AdvancedMutationPipeline(
                        data, orderAuth);
        var placed =
                pipeline.placeOrder("alice", 1, 4, 2, "demo-place-order-1")
                        .toCompletableFuture()
                        .join();
        var replay =
                pipeline.placeOrder("alice", 1, 4, 2, "demo-place-order-1")
                        .toCompletableFuture()
                        .join();
        System.out.println(
                "semantic entities="
                        + SupplyChainSemanticModel.MODEL.entities().size()
                        + ", contract="
                        + SupplyChainSemanticModel.MODEL.contractFingerprint());
        System.out.println(
                "runtime place_order plan="
                        + placed.planFingerprint()
                        + ", result="
                        + placed.resultFingerprint()
                        + ", replay-result="
                        + replay.resultFingerprint());
        var orderId =
                placed.result().results().getFirst().returnedValues().values().stream()
                        .findFirst()
                        .orElseThrow();
        var cancelled =
                pipeline.cancelOrder("alice", ((Number) orderId).intValue(), "demo-cancel-order-1")
                        .toCompletableFuture()
                        .join();
        var cancelReplay =
                pipeline.cancelOrder("alice", ((Number) orderId).intValue(), "demo-cancel-order-1")
                        .toCompletableFuture()
                        .join();
        System.out.println(
                "runtime cancel_order plan="
                        + cancelled.planFingerprint()
                        + ", result="
                        + cancelled.resultFingerprint()
                        + ", replay-result="
                        + cancelReplay.resultFingerprint());

        jpaDemo();
    }

    /**
     * JPA + PostgreSQL demo (docker compose up -d): Flyway migrates on start, then semantic reads with method
     * references and CRUD run through the fluent FoundgineOrm. Skipped when PostgreSQL is not reachable.
     */
    private static void jpaDemo() {
        var manager = new Authorization.Context(
                "tenant-a", Set.of(1, 2), Authorization.Role.SUPPLY_CHAIN_MANAGER, false);
        try (var jpa = new com.foundgine.samples.supplychain.advanced.jpa.AdvancedJpaReads();
                var em = jpa.openEntityManager()) {
            var reader = new com.foundgine.samples.supplychain.advanced.application.AdvancedReader(
                    jpa.orm().reader(em), manager);
            var warehouses = reader.from(com.foundgine.samples.supplychain.advanced.domain.Domain.Warehouse.class)
                    .select(com.foundgine.samples.supplychain.advanced.domain.Domain.Warehouse::id,
                            com.foundgine.samples.supplychain.advanced.domain.Domain.Warehouse::name)
                    .rows();
            System.out.println("tenant-a warehouses via JPA: " + warehouses.size());

            var crud = new com.foundgine.samples.supplychain.advanced.application.AdvancedCrud(jpa.orm(), em, manager);
            crud.create("Product", Map.of("id", 100, "sku", "DEMO-X", "name", "Demo Product", "category", "Demo",
                    "safetyStock", new java.math.BigDecimal("1.00"), "unitPrice", new java.math.BigDecimal("2.00")));
            crud.update("Product", 100, Map.of("unitPrice", new java.math.BigDecimal("3.00")));
            System.out.println("product after update: " + crud.get("Product", 100));
            crud.delete("Product", 100);
        } catch (RuntimeException e) {
            System.out.println("JPA demo skipped (is PostgreSQL running on SUPPLYCHAIN_JDBC_URL?): " + e);
        }
    }
}
