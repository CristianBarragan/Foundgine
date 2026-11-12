package com.foundgine.samples.supplychain.advanced.jpa;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.ProviderPlan;
import com.foundgine.core.semantic.ir.SemanticQuery;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;
import com.foundgine.providers.storage.orm.OrmRegistry;
import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;
import com.foundgine.providers.storage.orm.jpa.JpaOrmProvider;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;

import java.util.List;
import java.util.function.Consumer;
import java.util.stream.StreamSupport;

/**
 * Persistence-neutral facade over {@link AdvancedJpaOrm}: semantic read -> authorization -> plan -> ExecutionIR ->
 * provider plan, runnable against any {@code IOrmQuerySource}. The registry, contract, attribute validation and
 * fail-closed checks now live in {@link FoundgineOrm}; this class keeps the sample's name-based helpers and the
 * Warehouse -> InventoryLot read.
 */
public final class AdvancedJpaSemantics {
    private final FoundgineOrm<Authorization.Context> orm = AdvancedJpaOrm.build();
    private final RelationshipMetadata inventory;

    public AdvancedJpaSemantics(String provider) {
        if (!JpaOrmProvider.PROVIDER.equals(provider)) {
            throw new IllegalArgumentException("FoundgineOrm plans for provider '" + JpaOrmProvider.PROVIDER + "', not '" + provider + "'.");
        }
        var warehouse = entity("Warehouse").entityId();
        var lot = entity("InventoryLot").entityId();
        this.inventory = StreamSupport.stream(orm.metadata().relationships().spliterator(), false)
                .filter(r -> r.source().equals(warehouse) && r.target().equals(lot)).findFirst()
                .orElseThrow(() -> new IllegalStateException("The model declares no Warehouse -> InventoryLot relationship."));
    }

    public FoundgineOrm<Authorization.Context> orm() {
        return orm;
    }

    public RelationshipMetadata inventoryRelationship() {
        return inventory;
    }

    public OrmRegistry registry() {
        return orm.registry();
    }

    public EntityMetadata entity(String name) {
        return StreamSupport.stream(orm.metadata().entities().spliterator(), false)
                .filter(x -> x.name().equals(name)).findFirst()
                .orElseThrow(() -> new IllegalArgumentException("Unknown entity " + name));
    }

    public FieldId field(String entity, String field) {
        return entity(entity).effectiveFields().stream().filter(f -> f.name().equalsIgnoreCase(field)).findFirst()
                .orElseThrow(() -> new IllegalArgumentException("Unknown field " + entity + "." + field)).id();
    }

    /** The warehouse + nested lots query, usable with any reader or plan call. */
    public Consumer<SemanticQuery> warehouseInventory(
            List<String> warehouseFields, List<String> lotFields, String filterField, Object value) {
        return q -> q.select(warehouseFields)
                .whereEq(filterField, value)
                .include(inventory.id(), lots -> lots.select(lotFields));
    }

    /** Warehouses where {@code filterField = value}, each with its inventory lots; authorized for {@code caller}. */
    public ProviderPlan planWarehouseInventory(
            Authorization.Context caller, List<String> warehouseFields, List<String> lotFields,
            String filterField, Object value) {
        return planRead(caller, "Warehouse", warehouseInventory(warehouseFields, lotFields, filterField, value));
    }

    /** Lambda form (the Java twin of C# {@code reader.ReadAsync(entity, q => ...)}). */
    public ProviderPlan planRead(Authorization.Context caller, String entity, Consumer<SemanticQuery> configure) {
        return orm.plan(caller, entity, configure);
    }
}
