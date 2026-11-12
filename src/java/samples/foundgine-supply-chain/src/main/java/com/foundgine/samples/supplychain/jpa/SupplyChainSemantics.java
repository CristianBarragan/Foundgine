package com.foundgine.samples.supplychain.jpa;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.ExecutionIRCompiler;
import com.foundgine.core.execution.ProviderPlan;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.authorization.SemanticAuthorizer;
import com.foundgine.core.semantic.ir.SemanticQuery;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.metadata.SemanticModelDiscovery;
import com.foundgine.core.semantic.planning.Planner;
import com.foundgine.samples.supplychain.Authorization;
import com.foundgine.samples.supplychain.generated.GeneratedFoundgineMetadata;
import com.foundgine.providers.storage.orm.OrmPlanCompiler;
import com.foundgine.providers.storage.orm.OrmRegistry;

import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.stream.StreamSupport;

/**
 * The persistence-neutral half of the read path: generated metadata -> ORM registry -> frozen contract, and
 * semantic read -> authorization (with provenance) -> plan -> ExecutionIR -> provider plan. It has no JPA
 * dependency, so it can be exercised against any {@code IOrmQuerySource}. It is the hand-written baseline; the JPA
 * path is {@link SupplyChainOrm}, wrapped by {@link SupplyChainJpa}.
 *
 * <p>The contract is scoped to the mapped entities: the generated metadata for the full domain cannot be turned
 * into a semantic contract because {@code ProductComponent} has a composite key and no {@code id} field, and
 * the ORM provider can only execute mapped entities anyway.
 *
 * <p>Generated semantic field names are PascalCase ({@code RiskScore}); JPA attributes are camelCase
 * ({@code riskScore}). The registry is given that mapping explicitly, and callers may use either casing.
 */
public final class SupplyChainSemantics {
    /** Semantic entity name -> persistent class. Only these entities are queryable through the ORM provider. */
    public static final Map<String, Class<?>> ENTITY_TYPES = Map.of(
            "Supplier", SupplierEntity.class,
            "Warehouse", WarehouseEntity.class,
            "Product", ProductEntity.class);

    private final String provider;
    /** Only the mapped entities (and relationships between them); see the class comment for why. */
    private final MetadataRegistry metadata = scoped(GeneratedFoundgineMetadata.build());
    private final OrmRegistry registry;
    private final SemanticContractSnapshot contract;
    private final Planner planner = new Planner();

    public SupplyChainSemantics(String provider) {
        this.provider = provider;
        var mapped = StreamSupport.stream(metadata.entities().spliterator(), false).toList();
        var relationships = StreamSupport.stream(metadata.relationships().spliterator(), false).toList();
        var attributeNames = new HashMap<String, String>();
        for (var e : mapped) {
            for (var f : e.effectiveFields()) attributeNames.put(e.name() + "." + f.name(), decapitalize(f.name()));
        }
        this.registry = OrmRegistry.create(mapped, relationships, ENTITY_TYPES, attributeNames);
        requireAttributes(registry);
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

    /**
     * Plans a read of {@code fields} from {@code entity} where {@code filterField = value}, authorized for
     * {@code caller} by {@link SupplyChainPolicy}. Execute the result with {@link SupplyChainPolicy#executionContext}.
     */
    public ProviderPlan planRead(
            Authorization.Context caller, String entity, List<String> fields, String filterField, Object value) {
        return planRead(caller, entity, q -> q.select(fields).whereEq(filterField, value));
    }

    /** Lambda form (the Java twin of C# {@code reader.ReadAsync(entity, q => ...)}): any query, authorized for {@code caller}. */
    public ProviderPlan planRead(
            Authorization.Context caller, String entity, java.util.function.Consumer<SemanticQuery> configure) {
        var query = SemanticQuery.from(metadata, entity);
        configure.accept(query);
        var authorizer = new SemanticAuthorizer(SupplyChainPolicy.create(this, caller));
        var semanticPlan = planner.plan(contract, authorizer.authorizeWithEvidence(contract, query.build()));
        return new OrmPlanCompiler(provider, registry).compile(ExecutionIRCompiler.compile(semanticPlan));
    }

    /**
     * Generic form of {@link #planRead(Authorization.Context, String, List, String, Object)}: any number of
     * field = value filters (ANDed), addressed by field id. Used by the generic entity gateway.
     */
    public ProviderPlan planRead(
            Authorization.Context caller, EntityId entity, List<FieldId> selected, Map<FieldId, Object> equals) {
        var query = SemanticQuery.from(metadata, entity).select(selected.toArray(FieldId[]::new));
        equals.forEach(query::whereEq);
        var operation = query.build();
        var authorizer = new SemanticAuthorizer(SupplyChainPolicy.create(this, caller));
        var semanticPlan = planner.plan(contract, authorizer.authorizeWithEvidence(contract, operation));
        return new OrmPlanCompiler(provider, registry).compile(ExecutionIRCompiler.compile(semanticPlan));
    }

    /** The provider does not check this: without it a misnamed attribute only fails on the first query. */
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
