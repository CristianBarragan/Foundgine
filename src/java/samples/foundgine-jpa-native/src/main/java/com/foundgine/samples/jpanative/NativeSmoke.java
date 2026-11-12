package com.foundgine.samples.jpanative;

import com.foundgine.samples.supplychain.Authorization;
import com.foundgine.samples.supplychain.jpa.SupplierEntity;
import com.foundgine.samples.supplychain.jpa.SupplyChainOrm;

import jakarta.persistence.Persistence;

import java.math.BigDecimal;
import java.util.Map;
import java.util.Set;

/**
 * Smoke path for JVM and Native Image: H2 in memory, create the schema through Hibernate, insert through the
 * generated-model repository, then read through the authorized reader. Exits non-zero on any failure.
 * Run it once on the JVM before trusting the native result.
 *
 * <p>UNVERIFIED: never compiled or run (no JDK 27, GraalVM, Hibernate or H2 where it was written).
 */
public final class NativeSmoke {
    private NativeSmoke() {}

    public static void main(String[] args) {
        var orm = SupplyChainOrm.build();
        var props = Map.<String, Object>of(
                "jakarta.persistence.jdbc.url", "jdbc:h2:mem:smoke;DB_CLOSE_DELAY=-1",
                "jakarta.persistence.jdbc.driver", "org.h2.Driver",
                "hibernate.hbm2ddl.auto", "create-drop");
        try (var emf = Persistence.createEntityManagerFactory("foundgine-supply-chain", props)) {
            try (var em = emf.createEntityManager()) {
                var repo = orm.repository(em, SupplierEntity.class);
                repo.add(new SupplierEntity(1, "Acme Components", "US", new BigDecimal("10.00"), "tenant-a"));
                repo.add(new SupplierEntity(2, "Other Tenant Ltd", "US", new BigDecimal("20.00"), "tenant-b"));
            }
            var caller = new Authorization.Context("tenant-a", Set.of(1, 2), Authorization.Role.ANALYST, true);
            var rows = orm.reader(emf).read(caller, "Supplier", q -> q.select("Id", "Name").whereEq("Country", "US"));
            if (rows.size() != 1 || !"Acme Components".equals(rows.get(0).values().get("name"))) {
                System.err.println("SMOKE FAIL: " + rows);
                System.exit(1);
            }
            System.out.println("SMOKE OK");
        }
    }
}
