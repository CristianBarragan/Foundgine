package com.foundgine.samples.supplychain.advanced.jpa;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.ExecutionIRCompiler;
import com.foundgine.core.execution.ProviderPlan;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.authorization.SemanticAuthorizer;
import com.foundgine.core.semantic.ir.SemanticQuery;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;
import com.foundgine.core.semantic.metadata.SemanticModelDiscovery;
import com.foundgine.core.semantic.planning.Planner;
import com.foundgine.providers.storage.orm.OrmPlanCompiler;
import com.foundgine.providers.storage.orm.OrmRegistry;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.model.SupplyChainFoundgineModel;

import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.stream.StreamSupport;

/**
 * Persistence-neutral half of the advanced JPA read path: fluent model metadata -> ORM registry -> frozen
 * contract, then semantic read -> authorization -> plan -> ExecutionIR -> provider plan. No JPA dependency, so it
 * runs against any {@code IOrmQuerySource}; {@link AdvancedJpaReads} adds JPA.
 *
 * <p>Unlike the starter (annotation metadata), the relationship Warehouse -> InventoryLot comes from the fluent
 * model's connections, so nested reads work without adding components to the domain records.
 *
 * <p>The contract is scoped to the mapped entities. Semantic field names are PascalCase; JPA attributes are
 * camelCase, and the registry is given that mapping explicitly.
 */
public final class AdvancedJpaSemantics {
    public static final Map<String, Class<?>> ENTITY_TYPES = Map.of(
            "Warehouse", WarehouseEntity.class,
            "InventoryLot", InventoryLotEntity.class);

    private final String provider;
    private final MetadataRegistry metadata = scoped(new SupplyChainFoundgineModel().build());
    private final OrmRegistry registry;
    private final SemanticContractSnapshot contract;
    private final RelationshipMetadata inventory;
    private final Planner planner = new Planner();

    public AdvancedJpaSemantics(String provider) {
        this.provider = provider;
        var entities = StreamSupport.stream(metadata.entities().spliterator(), false).toList();
        var relationships = StreamSupport.stream(metadata.relationships().spliterator(), false).toList();
        var attributeNames = new HashMap<String, String>();
        for (var e : entities) {
            for (var f : e.effectiveFields()) attributeNames.put(e.name() + "." + f.name(), decapitalize(f.name()));
        }
        this.registry = OrmRegistry.create(entities, relationships, ENTITY_TYPES, attributeNames);
        requireAttributes(registry);
        var warehouse = entity("Warehouse").entityId();
        var lot = entity("InventoryLot").entityId();
        this.inventory = relationships.stream()
                .filter(r -> r.source().equals(warehouse) && r.target().equals(lot)).findFirst()
                .orElseThrow(() -> new IllegalStateException("The model declares no Warehouse -> InventoryLot relationship."));
        this.contract = SemanticModelDiscovery.fromMetadata(metadata).build().freeze().createSnapshot();
    }

    public OrmRegistry registry() {
        return registry;
    }

    public EntityMetadata entity(String name) {
        return StreamSupport.stream(metadata.entities().spliterator(), false)
                .filter(x -> x.name().equals(name)).findFirst()
                .orElseThrow(() -> new IllegalArgumentException("Unknown entity " + name));
    }

    public FieldId field(String entity, String field) {
        return entity(entity).effectiveFields().stream().filter(f -> f.name().equalsIgnoreCase(field)).findFirst()
                .orElseThrow(() -> new IllegalArgumentException("Unknown field " + entity + "." + field)).id();
    }

    /** Warehouses where {@code filterField = value}, each with its inventory lots; authorized for {@code caller}. */
    public ProviderPlan planWarehouseInventory(
            Authorization.Context caller, List<String> warehouseFields, List<String> lotFields,
            String filterField, Object value) {
        return planRead(caller, "Warehouse", q -> q
                .select(warehouseFields)
                .whereEq(filterField, value)
                .include(inventory.id(), lots -> lots.select(lotFields)));
    }

    /** Lambda form (the Java twin of C# {@code reader.ReadAsync(entity, q => ...)}): any query, authorized for {@code caller}. */
    public ProviderPlan planRead(
            Authorization.Context caller, String entity, java.util.function.Consumer<SemanticQuery> configure) {
        var query = SemanticQuery.from(metadata, entity);
        configure.accept(query);
        var authorizer = new SemanticAuthorizer(AdvancedJpaPolicy.create(this, caller));
        var plan = planner.plan(contract, authorizer.authorizeWithEvidence(contract, query.build()));
        return new OrmPlanCompiler(provider, registry).compile(ExecutionIRCompiler.compile(plan));
    }

    private static void requireAttributes(OrmRegistry registry) {
        for (var mapping : registry.all()) {
            for (var binding : mapping.fields()) {
                if (!hasField(mapping.type(), binding.name())) {
                    throw new IllegalStateException("Semantic entity '" + mapping.name() + "' field " + binding.id()
                            + " is bound to attribute '" + binding.name() + "', which "
                            + mapping.type().getSimpleName() + " does not declare.");
                }
            }
        }
    }

    private static boolean hasField(Class<?> type, String name) {
        for (Class<?> c = type; c != null; c = c.getSuperclass()) {
            try {
                c.getDeclaredField(name);
                return true;
            } catch (NoSuchFieldException ignored) {
                // keep walking up
            }
        }
        return false;
    }

    private static MetadataRegistry scoped(MetadataRegistry all) {
        var out = new MetadataRegistry();
        var ids = new HashSet<EntityId>();
        for (var e : all.entities()) {
            if (ENTITY_TYPES.containsKey(e.name())) {
                out.register(e);
                ids.add(e.entityId());
            }
        }
        for (var r : all.relationships()) {
            if (ids.contains(r.source()) && ids.contains(r.target())) out.register(r);
        }
        return out;
    }

    private static String decapitalize(String s) {
        return s.isEmpty() ? s : Character.toLowerCase(s.charAt(0)) + s.substring(1);
    }
}
