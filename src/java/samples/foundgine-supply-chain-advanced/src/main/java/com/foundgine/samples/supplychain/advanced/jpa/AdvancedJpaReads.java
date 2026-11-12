package com.foundgine.samples.supplychain.advanced.jpa;

import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.providers.storage.orm.jpa.JpaOrmProvider;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;

import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Persistence;

import java.util.List;
import java.util.Map;

/**
 * JPA + PostgreSQL execution for the advanced sample's read path. {@link AdvancedJpaSemantics} does the Foundgine
 * half; this class only opens JPA and runs the plan through the JPA Criteria adapter on Hibernate.
 *
 * <p>UNVERIFIED: not compiled or run against Hibernate/PostgreSQL where it was written (no jars, no network).
 */
public final class AdvancedJpaReads implements AutoCloseable {
    private final EntityManagerFactory emf;
    private final AdvancedJpaSemantics semantics = new AdvancedJpaSemantics(JpaOrmProvider.PROVIDER);

    public AdvancedJpaReads() {
        this.emf = Persistence.createEntityManagerFactory("foundgine-supply-chain-advanced", connectionProperties());
    }

    /** Warehouses where {@code filterField = value} with their inventory lots, as {@code caller}. */
    public List<ExecutionRow> warehouseInventory(
            Authorization.Context caller, List<String> warehouseFields, List<String> lotFields,
            String filterField, Object value) {
        return semantics.orm().reader(emf).read(caller, "Warehouse",
                semantics.warehouseInventory(warehouseFields, lotFields, filterField, value));
    }

    /** The configured ORM: reader, typed repositories (e.g. {@code orm().repository(em, WarehouseEntity.class)}). */
    public com.foundgine.providers.storage.orm.jpa.FoundgineOrm<Authorization.Context> orm() {
        return semantics.orm();
    }

    public EntityManager openEntityManager() {
        return emf.createEntityManager();
    }

    private static Map<String, Object> connectionProperties() {
        return Map.of(
                "jakarta.persistence.jdbc.url", env("SUPPLYCHAIN_JDBC_URL", "jdbc:postgresql://localhost:4429/foundgine_supply_chain"),
                "jakarta.persistence.jdbc.user", env("SUPPLYCHAIN_JDBC_USER", "foundgine"),
                "jakarta.persistence.jdbc.password", env("SUPPLYCHAIN_JDBC_PASSWORD", "foundgine"));
    }

    private static String env(String key, String fallback) {
        var v = System.getenv(key);
        return v == null || v.isBlank() ? fallback : v;
    }

    @Override
    public void close() {
        if (emf.isOpen()) emf.close();
    }
}
