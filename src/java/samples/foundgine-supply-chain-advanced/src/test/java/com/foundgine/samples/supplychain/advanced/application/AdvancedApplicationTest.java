package com.foundgine.samples.supplychain.advanced.application;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import com.foundgine.core.semantic.configuration.PropertyRef;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.domain.Domain.Product;
import com.foundgine.samples.supplychain.advanced.domain.Domain.Warehouse;
import com.foundgine.samples.supplychain.advanced.jpa.AdvancedJpaOrm;

import jakarta.persistence.EntityManager;

import java.lang.reflect.Proxy;
import java.util.Map;
import java.util.Set;

import org.junit.jupiter.api.Test;

/** Database-free checks for the method-reference reader and the CRUD write gate. Not executed where written. */
class AdvancedApplicationTest {
    @Test
    void methodReferencesBecomeSemanticFieldNames() {
        assertEquals("tenantId", AdvancedReader.fieldName((PropertyRef<Warehouse, String>) Warehouse::tenantId));
        assertEquals("businessUnitId", AdvancedReader.fieldName((PropertyRef<Warehouse, Integer>) Warehouse::businessUnitId));
        assertEquals("unitPrice", AdvancedReader.fieldName((PropertyRef<Product, Object>) Product::unitPrice));
    }

    @Test
    void plainLambdasAreRejected() {
        PropertyRef<Warehouse, Object> lambda = w -> w.name();
        assertThrows(IllegalArgumentException.class, () -> AdvancedReader.fieldName(lambda));
    }

    @Test
    void readOnlyAndNonWritingRolesCannotWrite() {
        var orm = AdvancedJpaOrm.build();
        var em = (EntityManager) Proxy.newProxyInstance(getClass().getClassLoader(),
                new Class<?>[] {EntityManager.class}, (p, m, a) -> { throw new AssertionError("must not be reached"); });
        var readOnly = new Authorization.Context("tenant-a", Set.of(1), Authorization.Role.SUPPLY_CHAIN_MANAGER, true);
        var analyst = new Authorization.Context("tenant-a", Set.of(1), Authorization.Role.ANALYST, false);
        for (var caller : new Authorization.Context[] {readOnly, analyst}) {
            var crud = new AdvancedCrud(orm, em, caller);
            assertThrows(SecurityException.class, () -> crud.create("Product", Map.of("id", 9, "sku", "X")));
            assertThrows(SecurityException.class, () -> crud.update("Warehouse", 1, Map.of("name", "x")));
            assertThrows(SecurityException.class, () -> crud.delete("InventoryLot", 900));
        }
    }

    @Test
    void lotsCannotBeListedWithoutNamingTheirWarehouse() {
        var orm = AdvancedJpaOrm.build();
        var em = (EntityManager) Proxy.newProxyInstance(getClass().getClassLoader(),
                new Class<?>[] {EntityManager.class}, (p, m, a) -> { throw new AssertionError("must not be reached"); });
        var crud = new AdvancedCrud(orm, em,
                new Authorization.Context("tenant-a", Set.of(1), Authorization.Role.ANALYST, false));
        assertThrows(IllegalArgumentException.class, () -> crud.list("InventoryLot", Map.of(), null));
    }
}
