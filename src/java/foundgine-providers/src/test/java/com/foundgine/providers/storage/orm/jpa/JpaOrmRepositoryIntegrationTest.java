package com.foundgine.providers.storage.orm.jpa;

import static org.junit.jupiter.api.Assertions.*;

import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Persistence;
import java.util.Map;
import java.util.function.BiConsumer;
import java.util.function.Function;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

/** Exercises the typed repository against Hibernate/H2 rather than a mocked JPA API. */
class JpaOrmRepositoryIntegrationTest {
    private static EntityManagerFactory factory;

    @BeforeAll
    static void open() { factory = Persistence.createEntityManagerFactory("foundgine-jpa-baseline"); }

    @BeforeEach
    void clean() {
        try (EntityManager em = factory.createEntityManager()) {
            var tx = em.getTransaction(); tx.begin();
            em.createQuery("delete from JpaBaselineOrderLine").executeUpdate();
            em.createQuery("delete from JpaBaselineCustomer").executeUpdate();
            tx.commit();
        }
    }

    @AfterAll
    static void close() { if (factory != null && factory.isOpen()) factory.close(); }

    @Test
    void generatedAccessorRepositorySupportsCrudAndDetachesResults() {
        var model = new JpaEntityModel<>(JpaBaselineCustomer.class, JpaBaselineCustomer::new,
                Map.<String, Function<JpaBaselineCustomer, Object>>of(
                        "id", JpaBaselineCustomer::getId,
                        "name", JpaBaselineCustomer::getName,
                        "region", JpaBaselineCustomer::getRegion,
                        "enabled", JpaBaselineCustomer::getEnabled),
                Map.<String, BiConsumer<JpaBaselineCustomer, Object>>of(
                        "id", (e, v) -> e.setId((Long) v),
                        "name", (e, v) -> e.setName((String) v),
                        "region", (e, v) -> e.setRegion((String) v),
                        "enabled", (e, v) -> e.setEnabled((Boolean) v)));

        try (EntityManager em = factory.createEntityManager()) {
            var repository = new JpaOrmRepository<>(em, model);
            var created = repository.add(new JpaBaselineCustomer(9901L, "Typed", "west"));
            assertFalse(em.contains(created), "insert result should be detached after the repository operation");
            assertEquals("Typed", repository.get(9901L).getName());
            assertEquals(1, repository.count());
            assertTrue(repository.exists(root -> em.getCriteriaBuilder().equal(root.get("region"), "west")));
            assertEquals(1, repository.list(root -> em.getCriteriaBuilder().equal(root.get("name"), "Typed")).size());

            assertEquals(1, repository.update(9901L, Map.of("name", "Updated", "enabled", true)));
            assertEquals("Updated", repository.get(9901L).getName());
            assertThrows(IllegalArgumentException.class, () -> repository.update(9901L, Map.of("id", 7L)));
            assertEquals(1, repository.delete(9901L));
            assertNull(repository.get(9901L));
            assertEquals(0, repository.delete(9901L));
        }
    }

    @Test
    void singleAttributeUpdateSetsAndClearsValuesAndRejectsBadInput() {
        try (EntityManager em = factory.createEntityManager()) {
            var repository = new JpaOrmRepository<>(em, JpaBaselineCustomer.class);
            repository.add(new JpaBaselineCustomer(9903L, "Single", "south"));

            assertEquals(1, repository.update(9903L, "region", "east"));
            assertEquals("east", repository.get(9903L).getRegion());

            assertEquals(1, repository.update(9903L, "region", null), "a null value must clear the column");
            assertNull(repository.get(9903L).getRegion());

            assertEquals(0, repository.update(424242L, "region", "east"), "unknown key updates no rows");
            assertThrows(IllegalArgumentException.class, () -> repository.update(9903L, "region", 42));
            assertThrows(IllegalArgumentException.class, () -> repository.update(9903L, "noSuchAttribute", "x"));
            assertThrows(IllegalArgumentException.class, () -> repository.update(9903L, "id", 7L));
            assertThrows(NullPointerException.class, () -> repository.update(9903L, (String) null, "x"));
        }
    }

    @Test
    void repositoryJoinsCallerTransactionWithoutCommittingIt() {
        try (EntityManager em = factory.createEntityManager()) {
            var repository = new JpaOrmRepository<>(em, JpaBaselineCustomer.class);
            var tx = em.getTransaction();
            tx.begin();
            repository.add(new JpaBaselineCustomer(9902L, "CallerTx", "north"));
            assertTrue(tx.isActive(), "repository must not commit a caller-owned transaction");
            tx.rollback();
            assertNull(em.find(JpaBaselineCustomer.class, 9902L));
        }
    }
}
