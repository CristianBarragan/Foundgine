package com.foundgine.samples.supplychain;

import com.foundgine.core.semantic.query.SemanticSortDirection;
import com.foundgine.runtime.*;
import com.foundgine.samples.supplychain.application.SupplyChainCrud;
import com.foundgine.samples.supplychain.application.SupplyChainReader;
import com.foundgine.samples.supplychain.domain.Domain.Supplier;
import com.foundgine.samples.supplychain.jpa.SupplyChainJpa;

import java.math.BigDecimal;
import java.util.Map;
import java.util.Set;

public final class Main {
    public static void main(String[] args) {
        FoundgineOptions options = new FoundgineOptions();
        System.out.println("Foundgine Supply Chain Java sample");
        System.out.println("Runtime configured: " + options);

        // Start the database first (docker compose up -d). SupplyChainJpa applies the Flyway migrations on start
        // (SUPPLYCHAIN_MIGRATE_ON_START) and the demo rows (SUPPLYCHAIN_SEED_DEMO).
        var manager = new Authorization.Context("tenant-a", Set.of(1, 2), Authorization.Role.SUPPLY_CHAIN_MANAGER, false);
        try (var jpa = new SupplyChainJpa(); var em = jpa.openEntityManager()) {
            // Semantic read written with method references; tenant isolation comes from the policy, not this filter.
            var reader = new SupplyChainReader(jpa.orm().reader(em), manager);
            var suppliers = reader.from(Supplier.class)
                    .eq(Supplier::country, "US")
                    .select(Supplier::id, Supplier::name, Supplier::country, Supplier::riskScore)
                    .orderBy(Supplier::name, SemanticSortDirection.ASC)
                    .rows();
            System.out.println("tenant-a US suppliers: " + suppliers.size());
            suppliers.forEach(r -> System.out.println("  " + r.values()));

            // CRUD through the same fluent configuration: reads are authorized, writes need canWrite.
            var crud = new SupplyChainCrud(jpa.orm(), em, manager);
            crud.create("Supplier", Map.of("id", 100, "name", "Demo Supplies", "country", "NZ", "riskScore", new BigDecimal("7.50")));
            System.out.println("created: " + crud.get("Supplier", 100));
            crud.update("Supplier", 100, Map.of("riskScore", new BigDecimal("9.00")));
            System.out.println("updated: " + crud.get("Supplier", 100));
            crud.delete("Supplier", 100);
            System.out.println("deleted, now visible: " + (crud.get("Supplier", 100) != null));
        } catch (RuntimeException e) {
            System.out.println("JPA demo skipped (is PostgreSQL running on SUPPLYCHAIN_JDBC_URL?): " + e);
        }
    }
}
