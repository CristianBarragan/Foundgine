package com.foundgine.samples.supplychain.advanced.jpa;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.generated.GeneratedJpaModels;
import com.foundgine.samples.supplychain.advanced.generated.GeneratedOrmMaps;
import com.foundgine.samples.supplychain.advanced.model.SupplyChainFoundgineModel;

import java.util.Set;

import org.junit.jupiter.api.Test;

/**
 * Database-free checks for the advanced {@link FoundgineOrm}: generated maps cover both mapped entities with an
 * identifier, the build fails closed, and the model-declared relationship is reachable through the plan path.
 * Test definitions only: not executed where they were written.
 */
class AdvancedJpaOrmTest {
    private static Authorization.Context caller(Authorization.Role role) {
        return new Authorization.Context("tenant-a", Set.of(1, 2), role, true);
    }

    @Test
    void generatedMapsAndModelsCoverBothEntities() {
        assertEquals(Set.of("Warehouse", "InventoryLot"), GeneratedOrmMaps.entityTypes().keySet());
        var orm = AdvancedJpaOrm.build();
        assertEquals(2, orm.registry().all().size());
        for (var mapping : orm.registry().all()) {
            assertEquals("id", orm.model(mapping).idAttribute(), mapping.name());
        }
    }

    @Test
    void buildFailsClosedWithoutAuthorizationOrGeneratedModels() {
        var bare = FoundgineOrm.<Authorization.Context>configure()
                .metadata(new SupplyChainFoundgineModel().build())
                .entities(GeneratedOrmMaps.entityTypes())
                .attributeNaming(FoundgineOrm.AttributeNaming.CAMEL_CASE);
        assertTrue(assertThrows(IllegalStateException.class,
                () -> bare.generatedModels(GeneratedJpaModels.create()).build()).getMessage().contains("authorization"));
        var withoutModels = FoundgineOrm.<Authorization.Context>configure()
                .metadata(new SupplyChainFoundgineModel().build())
                .entities(GeneratedOrmMaps.entityTypes())
                .attributeNaming(FoundgineOrm.AttributeNaming.CAMEL_CASE)
                .authorization(AdvancedJpaOrm.AUTHORIZATION);
        assertTrue(assertThrows(IllegalStateException.class, withoutModels::build).getMessage().contains("generated model"));
    }

    @Test
    void nestedPlanUsesTheModelRelationship() {
        var semantics = new AdvancedJpaSemantics("orm-jpa");
        assertNotNull(semantics.inventoryRelationship());
        assertNotNull(semantics.planWarehouseInventory(caller(Authorization.Role.ANALYST),
                java.util.List.of("id", "name"), java.util.List.of("id", "onHand"), "tenantId", "tenant-a"));
    }
}
