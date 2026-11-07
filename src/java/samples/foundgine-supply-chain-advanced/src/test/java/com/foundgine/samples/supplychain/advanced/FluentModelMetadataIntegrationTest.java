package com.foundgine.samples.supplychain.advanced;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;
import com.foundgine.samples.supplychain.advanced.model.SupplyChainFoundgineModel;

import org.junit.jupiter.api.Test;

import java.util.Map;
import java.util.stream.StreamSupport;

/**
 * Proves the Advanced Supply Chain sample is described entirely by its fluent configuration: the
 * domain records carry no Foundgine annotations, and every entity, alias, event, relationship and
 * key comes from {@link SupplyChainFoundgineModel}.
 */
class FluentModelMetadataIntegrationTest {
    private static EntityMetadata entity(MetadataRegistry registry, String name) {
        return StreamSupport.stream(registry.entities().spliterator(), false)
                .filter(e -> e.name().equals(name))
                .findFirst()
                .orElseThrow();
    }

    @Test
    void registryContainsConfiguredSupplyChainEntities() {
        var registry = new SupplyChainFoundgineModel().build();
        var entities =
                StreamSupport.stream(registry.entities().spliterator(), false)
                        .map(EntityMetadata::name)
                        .toList();

        assertTrue(entities.contains("Company"));
        assertTrue(entities.contains("Supplier"));
        assertTrue(entities.contains("PurchaseOrder"));
        assertTrue(entities.contains("InventoryMovement"));
        assertTrue(entities.contains("ComplianceIncident"));
        assertFalse(entities.isEmpty());
    }

    @Test
    void configuredMetadataPreservesAliasesAndEventMetadata() {
        var registry = new SupplyChainFoundgineModel().build();
        var supplier = entity(registry, "Supplier");

        assertTrue(
                supplier.aliases().stream()
                        .anyMatch(a -> a.name().equals("Vendor") && a.weight() == 95));

        var movement = entity(registry, "InventoryMovement");
        assertTrue(movement.event());
        assertNotNull(movement.temporalColumn());

        // An event without a declared timestamp member has no temporal column.
        var output = entity(registry, "ProductionOutput");
        assertTrue(output.event());
        assertNull(output.temporalColumn());
    }

    @Test
    void entityIdentitiesAreDerivedFromSemanticNames() {
        var registry = new SupplyChainFoundgineModel().build();
        for (var e : registry.entities())
            assertEquals(EntityId.create(e.name()), e.entityId(), e.name());
    }

    @Test
    void keysFollowConventionExplicitDeclarationAndIndexes() {
        var registry = new SupplyChainFoundgineModel().build();

        // "id" is the key unless the persistence configuration names another member.
        var supplier = entity(registry, "Supplier");
        assertEquals(ColumnId.create("Supplier", "id"), supplier.primaryKey().columnId());
        var component = entity(registry, "ProductComponent");
        assertEquals(
                ColumnId.create("ProductComponent", "parentProductId"),
                component.primaryKey().columnId());

        FieldMetadata companyId =
                entity(registry, "BusinessUnit").effectiveFields().stream()
                        .filter(f -> f.name().equals("CompanyId"))
                        .findFirst()
                        .orElseThrow();
        assertTrue(companyId.indexed());
        assertEquals("company", companyId.dimension());
    }

    @Test
    void relationshipsKeepCardinalityOrientationAndNames() {
        var registry = new SupplyChainFoundgineModel().build();
        var byName =
                StreamSupport.stream(registry.relationships().spliterator(), false)
                        .collect(
                                java.util.stream.Collectors.toMap(
                                        r -> registry.get(r.source()).name() + "." + r.name(),
                                        r -> r));

        // Source key / target key / cardinality, as declared on the source entity.
        Map<String, Object[]> expected =
                Map.ofEntries(
                        Map.entry("Supplier.purchaseOrders", new Object[] {"id", "supplierId", true}),
                        Map.entry("PurchaseOrder.supplier", new Object[] {"supplierId", "id", false}),
                        Map.entry("Warehouse.inventory", new Object[] {"id", "warehouseId", true}),
                        Map.entry("InventoryLot.warehouse", new Object[] {"warehouseId", "id", false}),
                        Map.entry("Warehouse.businessUnit", new Object[] {"businessUnitId", "id", false}),
                        Map.entry("Product.components", new Object[] {"id", "parentProductId", true}),
                        Map.entry(
                                "Product.usedInComponents",
                                new Object[] {"id", "componentProductId", true}),
                        Map.entry(
                                "ProductComponent.componentProduct",
                                new Object[] {"componentProductId", "id", false}),
                        Map.entry("PurchaseOrder.lines", new Object[] {"id", "purchaseOrderId", true}),
                        Map.entry(
                                "PurchaseOrderLine.purchaseOrder",
                                new Object[] {"purchaseOrderId", "id", false}));

        for (var entry : expected.entrySet()) {
            RelationshipMetadata r = byName.get(entry.getKey());
            assertNotNull(r, entry.getKey());
            var source = registry.get(r.source());
            var target = registry.get(r.target());
            assertEquals(
                    ColumnId.create(source.storageName(), (String) entry.getValue()[0]),
                    r.sourceKey().columnId(),
                    entry.getKey());
            assertEquals(
                    ColumnId.create(target.storageName(), (String) entry.getValue()[1]),
                    r.targetKey().columnId(),
                    entry.getKey());
            assertEquals(entry.getValue()[2], r.collection(), entry.getKey());
        }
    }
}
