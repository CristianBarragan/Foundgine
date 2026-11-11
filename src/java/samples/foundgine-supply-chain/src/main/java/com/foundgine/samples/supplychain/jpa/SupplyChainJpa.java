package com.foundgine.samples.supplychain.jpa;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.providers.storage.orm.jpa.JpaEntities;
import com.foundgine.providers.storage.orm.jpa.JpaOrmProvider;
import com.foundgine.samples.supplychain.Authorization;

import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Persistence;

import java.util.List;
import java.util.Map;

/**
 * JPA + PostgreSQL execution for the starter. {@link SupplyChainSemantics} does the Foundgine half (authorize,
 * plan, lower to a provider plan); this class only opens JPA and runs the plan through the JPA Criteria adapter
 * on Hibernate. Foundgine, not JPA, owns planning and authorization.
 *
 * <p>UNVERIFIED: the Foundgine half is exercised against the in-memory ORM source, but this class itself has
 * not been compiled or run against Hibernate/PostgreSQL (no JPA 3.2 jars or network where it was written).
 */
public final class SupplyChainJpa implements AutoCloseable {
    private final EntityManagerFactory emf;
    private final SupplyChainSemantics semantics = new SupplyChainSemantics(JpaOrmProvider.PROVIDER);

    public SupplyChainJpa() {
        this.emf = Persistence.createEntityManagerFactory("foundgine-supply-chain", connectionProperties());
    }

    /**
     * Reads {@code entity} rows (the listed fields) where {@code filterField = value}, as {@code caller}: the
     * policy adds tenant isolation and drops fields the caller may not see. Field names are case-insensitive.
     */
    public List<ExecutionRow> read(
            Authorization.Context caller, String entity, List<String> fields, String filterField, Object value) {
        var providerPlan = semantics.planRead(caller, entity, fields, filterField, value);
        try (EntityManager em = emf.createEntityManager()) {
            return JpaOrmProvider.execution(semantics.registry(), em)
                    .execute(providerPlan, SupplyChainPolicy.executionContext(caller), CancellationToken.NONE)
                    .rows();
        }
    }

    /**
     * Generic, name-addressed access to every mapped entity for {@code caller}. Reads go through the same
     * authorization as {@link #read}; writes are mechanical, so authorize the capability before calling them
     * (this sample's policy denies writes at the semantic layer, which does not cover the generic write methods).
     * The caller owns {@code em} and closes it.
     */
    public JpaEntities entities(Authorization.Context caller, EntityManager em) {
        return new JpaEntities(em, semantics.registry(), (entity, fields, equals) -> new JpaEntities.GatedRead(
                semantics.planRead(caller, entity.entityId(), fields, equals),
                SupplyChainPolicy.executionContext(caller)));
    }

    public EntityManager openEntityManager() {
        return emf.createEntityManager();
    }

    private static Map<String, Object> connectionProperties() {
        return Map.of(
                "jakarta.persistence.jdbc.url", env("SUPPLYCHAIN_JDBC_URL", "jdbc:postgresql://localhost:4429/foundgine_supply_chain"),
                "jakarta.persistence.jdbc.user", env("SUPPLYCHAIN_JDBC_USER", "benchmark"),
                "jakarta.persistence.jdbc.password", env("SUPPLYCHAIN_JDBC_PASSWORD", "benchmark"));
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
