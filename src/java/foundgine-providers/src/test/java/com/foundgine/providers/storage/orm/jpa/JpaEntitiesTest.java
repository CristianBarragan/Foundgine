package com.foundgine.providers.storage.orm.jpa;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.providers.storage.orm.OrmEntityMapping;
import com.foundgine.providers.storage.orm.OrmFieldBinding;
import com.foundgine.providers.storage.orm.OrmRegistry;

import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Persistence;

import java.util.List;
import java.util.Map;

import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

/**
 * The generic entity gateway against Hibernate + H2 (the JPA baseline persistence unit): name-addressed CRUD with no
 * per-entity code, key coercion, rejected unknown/key fields, and the read gate being the only way to read.
 */
class JpaEntitiesTest {
    private static EntityManagerFactory factory;
    private static final EntityId CUSTOMER = new EntityId(9100);
    private static final OrmEntityMapping MAPPING = new OrmEntityMapping(
            CUSTOMER, "JpaBaselineCustomer", JpaBaselineCustomer.class,
            List.of(
                    new OrmFieldBinding(new FieldId(9101), "id", Long.class, true, new ColumnId(9101)),
                    new OrmFieldBinding(new FieldId(9102), "name", String.class, false, new ColumnId(9102)),
                    new OrmFieldBinding(new FieldId(9103), "region", String.class, false, new ColumnId(9103)),
                    new OrmFieldBinding(new FieldId(9104), "enabled", Boolean.class, false, new ColumnId(9104))));

    private static OrmRegistry registry() {
        // The mapping is the registry entry; metadata-free construction is not exposed, so build through create().
        var fields = MAPPING.fields().stream()
                .map(b -> new com.foundgine.core.semantic.metadata.FieldMetadata(
                        b.id(), b.name(), b.type(),
                        new com.foundgine.core.semantic.metadata.ColumnReference(CUSTOMER, b.column()),
                        null, false, null))
                .toList();
        var entity = new com.foundgine.core.semantic.metadata.EntityMetadata(
                CUSTOMER, "JpaBaselineCustomer", List.of(), "jpa_baseline_customer", fields,
                new com.foundgine.core.semantic.metadata.ColumnReference(CUSTOMER, new ColumnId(9101)),
                JpaBaselineCustomer.class, false, null, null);
        return OrmRegistry.create(List.of(entity), List.of(), Map.of());
    }

    @BeforeAll
    static void openFactory() {
        factory = Persistence.createEntityManagerFactory("foundgine-jpa-baseline");
    }

    @AfterAll
    static void closeFactory() {
        if (factory != null && factory.isOpen()) factory.close();
    }

    @BeforeEach
    void clean() {
        try (EntityManager em = factory.createEntityManager()) {
            var tx = em.getTransaction();
            tx.begin();
            em.createQuery("delete from JpaBaselineOrderLine").executeUpdate();
            em.createQuery("delete from JpaBaselineCustomer").executeUpdate();
            tx.commit();
        }
    }

    private static JpaEntities.ReadGate denyAll() {
        return (entity, fields, equals) -> {
            throw new SecurityException("reads are denied in this test");
        };
    }

    @Test
    void entitiesAreAddressedByNameCaseInsensitively() {
        try (EntityManager em = factory.createEntityManager()) {
            var entities = new JpaEntities(em, registry(), denyAll());
            assertTrue(entities.has("jpabaselinecustomer"));
            assertEquals(List.of("JpaBaselineCustomer"), entities.names());
            assertEquals(List.of("id", "name", "region", "enabled"), entities.entity("JpaBaselineCustomer").fields());
            assertThrows(IllegalArgumentException.class, () -> entities.entity("Nope"));
        }
    }

    @Test
    void createGetUpdateDeleteRoundTripWithLooseValueTypes() {
        try (EntityManager em = factory.createEntityManager()) {
            var customers = new JpaEntities(em, registry(), denyAll()).entity("JpaBaselineCustomer");

            // id arrives as an Integer and enabled as a String: both are coerced to the mapped types.
            var created = (JpaBaselineCustomer) customers.create(
                    Map.of("id", 7, "name", "Ann", "region", "north", "enabled", "true"));
            assertEquals(7L, created.getId());
            assertTrue(created.getEnabled());

            var loaded = (JpaBaselineCustomer) customers.get(7); // Integer key -> Long
            assertNotNull(loaded);
            assertEquals("Ann", loaded.getName());

            assertEquals(1, customers.update(7, Map.of("region", "south")));
            em.clear();
            assertEquals("south", ((JpaBaselineCustomer) customers.get(7L)).getRegion());
            assertEquals(0, customers.update(99, Map.of("region", "x")));

            assertEquals(1, customers.delete(7));
            em.clear();
            assertNull(customers.get(7));
            assertEquals(0, customers.delete(7));
        }
    }

    @Test
    void unknownFieldsAndKeyUpdatesAreRejected() {
        try (EntityManager em = factory.createEntityManager()) {
            var customers = new JpaEntities(em, registry(), denyAll()).entity("JpaBaselineCustomer");
            customers.create(Map.of("id", 1, "name", "Ann", "enabled", false));

            assertThrows(IllegalArgumentException.class, () -> customers.create(Map.of("id", 2, "nope", 1)));
            assertThrows(IllegalArgumentException.class, () -> customers.update(1, Map.of("nope", 1)));
            assertThrows(IllegalArgumentException.class, () -> customers.update(1, Map.of("id", 99)));
        }
    }

    @Test
    void readsAlwaysGoThroughTheGate() {
        try (EntityManager em = factory.createEntityManager()) {
            var customers = new JpaEntities(em, registry(), denyAll()).entity("JpaBaselineCustomer");
            // The gate throws before any query is built: there is no ungated read path.
            assertThrows(SecurityException.class, () -> customers.read(Map.of("region", "north"), null));
        }
    }
}
