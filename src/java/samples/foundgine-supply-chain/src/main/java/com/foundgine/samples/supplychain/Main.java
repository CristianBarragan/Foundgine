package com.foundgine.samples.supplychain;

import com.foundgine.runtime.*;
import com.foundgine.samples.supplychain.jpa.SupplyChainJpa;

import java.util.List;
import java.util.Set;

public final class Main {
    public static void main(String[] args) {
        FoundgineOptions options = new FoundgineOptions();
        System.out.println("Foundgine Supply Chain Java sample");
        System.out.println("Runtime configured: " + options);

        // JPA + PostgreSQL demo: start the database first (docker compose up -d), then run this class.
        // The caller's tenant and role are enforced by SupplyChainPolicy, not by the filter below.
        var caller = new Authorization.Context("tenant-a", Set.of(1, 2), Authorization.Role.ANALYST, true);
        try (var jpa = new SupplyChainJpa()) {
            var suppliers = jpa.read(caller, "Supplier", List.of("id", "name", "country", "riskScore"), "country", "US");
            System.out.println("tenant-a US suppliers via JPA: " + suppliers.size());
            suppliers.forEach(r -> System.out.println("  " + r.values()));
        } catch (RuntimeException e) {
            System.out.println("JPA demo skipped (is PostgreSQL running on SUPPLYCHAIN_JDBC_URL?): " + e);
        }
    }
}
