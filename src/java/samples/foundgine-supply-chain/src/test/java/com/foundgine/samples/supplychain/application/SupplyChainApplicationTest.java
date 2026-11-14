package com.foundgine.samples.supplychain.application;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import com.foundgine.samples.supplychain.Authorization;
import com.foundgine.samples.supplychain.domain.Domain.Product;
import com.foundgine.samples.supplychain.domain.Domain.Supplier;
import com.foundgine.samples.supplychain.jpa.SupplyChainOrm;

import jakarta.persistence.EntityManager;

import java.lang.reflect.Proxy;
import java.util.Map;
import java.util.Set;

import org.junit.jupiter.api.Test;

/** Database-free checks for the method-reference reader and the CRUD write gate. Not executed where written. */
class SupplyChainApplicationTest {
    @Test
    void methodReferencesBecomeSemanticFieldNames() {
        assertEquals("country", SupplyChainReader.fieldName((com.foundgine.core.semantic.configuration.PropertyRef<Supplier, String>) Supplier::country));
        assertEquals("riskScore", SupplyChainReader.fieldName((com.foundgine.core.semantic.configuration.PropertyRef<Supplier, Object>) Supplier::riskScore));
        assertEquals("safetyStock", SupplyChainReader.fieldName((com.foundgine.core.semantic.configuration.PropertyRef<Product, Object>) Product::safetyStock));
    }

    @Test
    void plainLambdasAreRejected() {
        com.foundgine.core.semantic.configuration.PropertyRef<Supplier, Object> lambda = s -> s.country();
        assertThrows(IllegalArgumentException.class, () -> SupplyChainReader.fieldName(lambda));
    }

    @Test
    void readOnlyAndNonWritingRolesCannotWrite() {
        var orm = SupplyChainOrm.build();
        var em = (EntityManager) Proxy.newProxyInstance(getClass().getClassLoader(),
                new Class<?>[] {EntityManager.class}, (p, m, a) -> { throw new AssertionError("must not be reached"); });
        var readOnlyManager = new Authorization.Context("tenant-a", Set.of(1), Authorization.Role.SUPPLY_CHAIN_MANAGER, true);
        var analyst = new Authorization.Context("tenant-a", Set.of(1), Authorization.Role.ANALYST, false);
        for (var caller : new Authorization.Context[] {readOnlyManager, analyst}) {
            var crud = new SupplyChainCrud(orm, em, caller);
            assertThrows(SecurityException.class, () -> crud.create("Product", Map.of("id", 9, "sku", "X")));
            assertThrows(SecurityException.class, () -> crud.update("Product", 1, Map.of("name", "x")));
            assertThrows(SecurityException.class, () -> crud.delete("Product", 1));
        }
    }
}
