package com.foundgine.providers.storage.orm.jpa;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionIRCompiler;
import com.foundgine.core.execution.ProviderPlan;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.authorization.ISemanticAuthorizationPolicy;
import com.foundgine.core.semantic.authorization.SemanticAuthorizer;
import com.foundgine.core.semantic.ir.SemanticOperation;
import com.foundgine.core.semantic.ir.SemanticQuery;
import com.foundgine.core.semantic.planning.SemanticPlanFingerprint;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.metadata.SemanticModelDiscovery;
import com.foundgine.core.semantic.planning.Planner;
import com.foundgine.providers.storage.orm.OrmPlanCompiler;
import com.foundgine.providers.storage.orm.OrmRegistry;

import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;

import java.util.Collection;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.function.Consumer;
import java.util.stream.StreamSupport;

/**
 * Fluent, immutable JPA configuration for Foundgine: generated metadata + persistent classes + generated
 * direct-access models + a mandatory authorization binding. It replaces the per-application glue that the
 * supply-chain sample used to carry ({@code SupplyChainSemantics}).
 *
 * <pre>{@code
 * FoundgineOrm<Caller> orm = FoundgineOrm.<Caller>configure()
 *     .metadata(GeneratedFoundgineMetadata.build())
 *     .entities(GeneratedOrmMaps.entityTypes())
 *     .generatedModels(GeneratedJpaModels.create())
 *     .attributeNaming(FoundgineOrm.AttributeNaming.CAMEL_CASE)
 *     .authorization(new MyAuthorization())
 *     .build();
 * var rows = orm.reader(emf).read(caller, "Supplier", q -> q.select("Name").whereEq("Country", "NZ"));
 * }</pre>
 *
 * <p>Invariants: authorization is required (build fails closed without it); the contract holds only mapped
 * entities; every mapped entity needs a generated model unless {@link Builder#allowReflection()} is opted into
 * (JVM-only); attribute names are validated at build time.
 *
 * <p>UNVERIFIED: not compiled against the real Jakarta Persistence jars where it was written. See
 * ORM-JPA-PARITY-PLAN.md Step 72.
 */
public final class FoundgineOrm<C> {
    /** Binds a caller to authorization policy and execution context. Foundgine, not JPA, owns both. */
    public interface Authorization<C> {
        ISemanticAuthorizationPolicy policy(C caller, MetadataRegistry scopedMetadata);

        ExecutionContext executionContext(C caller);
    }

    /** How a semantic field name maps to a persistent attribute when no explicit override exists. */
    public enum AttributeNaming {
        /** attribute == semantic field name. */
        SAME_AS_FIELD,
        /** {@code RiskScore -> riskScore}. */
        CAMEL_CASE;

        String apply(String field) {
            if (this == SAME_AS_FIELD || field.isEmpty()) return field;
            return Character.toLowerCase(field.charAt(0)) + field.substring(1);
        }
    }

    private final MetadataRegistry metadata;
    private final OrmRegistry registry;
    private final SemanticContractSnapshot contract;
    private final Authorization<C> authorization;
    private final Map<Class<?>, JpaEntityModel<?>> models;
    private final boolean allowReflection;
    private final Planner planner = new Planner();

    private FoundgineOrm(MetadataRegistry metadata, OrmRegistry registry, SemanticContractSnapshot contract,
            Authorization<C> authorization, Map<Class<?>, JpaEntityModel<?>> models, boolean allowReflection) {
        this.metadata = metadata;
        this.registry = registry;
        this.contract = contract;
        this.authorization = authorization;
        this.models = models;
        this.allowReflection = allowReflection;
    }

    public static <C> Builder<C> configure() {
        return new Builder<>();
    }

    public OrmRegistry registry() { return registry; }

    /** Metadata scoped to the mapped entities (and relationships between them). */
    public MetadataRegistry metadata() { return metadata; }

    @SuppressWarnings("unchecked")
    public <T> JpaEntityModel<T> model(Class<T> type) { return (JpaEntityModel<T>) models.get(type); }

    public JpaEntityModel<?> model(com.foundgine.providers.storage.orm.OrmEntityMapping mapping) {
        return models.get(mapping.type());
    }

    /**
     * Authorizes and lowers a read to a provider plan without touching a database. Every read path goes through
     * here: policy -> authorization with evidence -> plan -> ExecutionIR -> provider plan.
     */
    public ProviderPlan plan(C caller, String entity, Consumer<SemanticQuery> configure) {
        Objects.requireNonNull(entity, "entity");
        Objects.requireNonNull(configure, "configure");
        var query = SemanticQuery.from(metadata, entity);
        configure.accept(query);
        return lower(caller, query);
    }

    /** Id-addressed form: any number of {@code field = value} filters, ANDed. */
    public ProviderPlan plan(C caller, EntityId entity, List<FieldId> selected, Map<FieldId, Object> equals) {
        var query = SemanticQuery.from(metadata, entity).select(selected.toArray(FieldId[]::new));
        equals.forEach(query::whereEq);
        return lower(caller, query);
    }

    /** An authorized, lowered read: the provider plan plus the fingerprint of the semantic plan that produced it. */
    public record LoweredRead(ProviderPlan plan, String fingerprint) {}

    /** Starts a fluent query over this ORM's metadata (C# {@code reader.From(entity)}). */
    public SemanticQuery from(EntityId entity) { return SemanticQuery.from(metadata, entity); }

    public SemanticQuery from(String entity) { return SemanticQuery.from(metadata, entity); }

    /** Authorizes and lowers an already-built operation (C# {@code ReadAsync(SemanticOperation)} without executing). */
    public LoweredRead lower(C caller, SemanticOperation operation) {
        Objects.requireNonNull(caller, "caller");
        Objects.requireNonNull(operation, "operation");
        var policy = Objects.requireNonNull(authorization.policy(caller, metadata), "authorization policy");
        var authorizer = new SemanticAuthorizer(policy);
        var semanticPlan = planner.plan(contract, authorizer.authorizeWithEvidence(contract, operation));
        var provider = new OrmPlanCompiler(JpaOrmProvider.PROVIDER, registry)
                .compile(ExecutionIRCompiler.compile(semanticPlan));
        return new LoweredRead(provider, SemanticPlanFingerprint.create(semanticPlan));
    }

    private ProviderPlan lower(C caller, SemanticQuery query) {
        Objects.requireNonNull(caller, "caller");
        var policy = Objects.requireNonNull(authorization.policy(caller, metadata), "authorization policy");
        var authorizer = new SemanticAuthorizer(policy);
        var semanticPlan = planner.plan(contract, authorizer.authorizeWithEvidence(contract, query.build()));
        return new OrmPlanCompiler(JpaOrmProvider.PROVIDER, registry).compile(ExecutionIRCompiler.compile(semanticPlan));
    }

    ExecutionContext executionContext(C caller) {
        return Objects.requireNonNull(authorization.executionContext(Objects.requireNonNull(caller, "caller")),
                "execution context");
    }

    /** Authorized semantic reader; the caller owns {@code em}. */
    public FoundgineJpaReader<C> reader(EntityManager em) {
        return new FoundgineJpaReader<>(this, Objects.requireNonNull(em, "em"), null);
    }

    /** Authorized semantic reader that opens (and closes) an EntityManager per read. */
    public FoundgineJpaReader<C> reader(EntityManagerFactory emf) {
        return new FoundgineJpaReader<>(this, null, Objects.requireNonNull(emf, "emf"));
    }

    /**
     * Typed CRUD repository. Mechanical, like the C# EF repository: it holds no authorization, so authorize the
     * capability before calling writes. Uses the generated model; the reflective path needs {@code allowReflection()}.
     */
    public <T> JpaOrmRepository<T> repository(EntityManager em, Class<T> type) {
        var model = model(type);
        if (model != null) return new JpaOrmRepository<>(em, model);
        if (allowReflection && registry.all().stream().anyMatch(m -> m.type() == type)) {
            return new JpaOrmRepository<>(em, type);
        }
        throw new IllegalArgumentException(type.getName() + " is not a mapped entity with a generated model.");
    }

    /** Name-addressed generic entity gateway whose reads are gated by {@code caller}'s authorization. */
    public JpaEntities entities(C caller, EntityManager em) {
        Objects.requireNonNull(caller, "caller");
        JpaEntities.ReadGate gate = (entity, fields, equals) ->
                new JpaEntities.GatedRead(plan(caller, entity.entityId(), fields, equals), executionContext(caller));
        return models.isEmpty() && allowReflection
                ? new JpaEntities(em, registry, gate)
                : new JpaEntities(em, registry, gate, models.values());
    }

    public static final class Builder<C> {
        private MetadataRegistry source;
        private Map<String, Class<?>> entityTypes = Map.of();
        private final List<JpaEntityModel<?>> generated = new java.util.ArrayList<>();
        private final Map<String, String> attributeOverrides = new HashMap<>();
        private AttributeNaming naming = AttributeNaming.SAME_AS_FIELD;
        private Authorization<C> authorization;
        private boolean allowReflection;

        private Builder() {}

        public Builder<C> metadata(MetadataRegistry metadata) { this.source = Objects.requireNonNull(metadata); return this; }

        /** Semantic entity name -> persistent class (see generated {@code GeneratedOrmMaps.entityTypes()}). */
        public Builder<C> entities(Map<String, Class<?>> types) { this.entityTypes = Map.copyOf(types); return this; }

        /** Registers compile-time generated direct-access models (see {@code GeneratedJpaModels.create()}). */
        public Builder<C> generatedModels(Collection<? extends JpaEntityModel<?>> models) { generated.addAll(models); return this; }

        public Builder<C> attributeNaming(AttributeNaming naming) { this.naming = Objects.requireNonNull(naming); return this; }

        /** Explicit attribute for one field; wins over the naming convention. */
        public Builder<C> attribute(String entity, String field, String attribute) {
            attributeOverrides.put(entity + "." + field, attribute);
            return this;
        }

        public Builder<C> authorization(Authorization<C> authorization) { this.authorization = authorization; return this; }

        /** JVM-only opt-in: lets entities without a generated model use reflective access. Not Native Image safe. */
        public Builder<C> allowReflection() { this.allowReflection = true; return this; }

        public FoundgineOrm<C> build() {
            if (authorization == null) {
                throw new IllegalStateException("FoundgineOrm requires an authorization binding; none was configured.");
            }
            if (source == null) throw new IllegalStateException("FoundgineOrm requires generated metadata.");
            if (entityTypes.isEmpty()) throw new IllegalStateException("FoundgineOrm requires at least one mapped entity.");

            var all = StreamSupport.stream(source.entities().spliterator(), false).toList();
            var known = new HashSet<String>();
            for (var e : all) known.add(e.name());
            for (String name : entityTypes.keySet()) {
                if (!known.contains(name)) {
                    throw new IllegalStateException("Mapped entity '" + name + "' is not in the semantic metadata.");
                }
            }
            var scoped = new MetadataRegistry();
            var ids = new HashSet<EntityId>();
            var mapped = new java.util.ArrayList<EntityMetadata>();
            for (var e : all) {
                if (entityTypes.containsKey(e.name())) { scoped.register(e); ids.add(e.entityId()); mapped.add(e); }
            }
            var relationships = new java.util.ArrayList<com.foundgine.core.semantic.metadata.RelationshipMetadata>();
            for (var r : source.relationships()) {
                if (ids.contains(r.source()) && ids.contains(r.target())) { scoped.register(r); relationships.add(r); }
            }

            var attributeNames = new HashMap<String, String>();
            for (var e : mapped) {
                for (var f : e.effectiveFields()) {
                    String key = e.name() + "." + f.name();
                    attributeNames.put(key, attributeOverrides.getOrDefault(key, naming.apply(f.name())));
                }
            }
            var registry = OrmRegistry.create(mapped, relationships, entityTypes, attributeNames);

            var models = new LinkedHashMap<Class<?>, JpaEntityModel<?>>();
            for (var m : generated) models.put(m.type(), m);
            for (var mapping : registry.all()) {
                var model = models.get(mapping.type());
                if (model == null && !allowReflection) {
                    throw new IllegalStateException("Mapped entity '" + mapping.name() + "' (" + mapping.type().getName()
                            + ") has no generated model. Add @FoundgineOrmEntity or opt in with allowReflection().");
                }
                for (var binding : mapping.fields()) {
                    boolean ok = model != null ? model.hasAttribute(binding.name()) : declares(mapping.type(), binding.name());
                    if (!ok) {
                        throw new IllegalStateException("Semantic entity '" + mapping.name() + "' field " + binding.id()
                                + " is bound to attribute '" + binding.name() + "', which "
                                + mapping.type().getSimpleName() + " does not declare.");
                    }
                }
            }
            var contract = SemanticModelDiscovery.fromMetadata(scoped).build().freeze().createSnapshot();
            return new FoundgineOrm<>(scoped, registry, contract, authorization, Map.copyOf(models), allowReflection);
        }

        private static boolean declares(Class<?> type, String name) {
            for (Class<?> c = type; c != null; c = c.getSuperclass()) {
                try { c.getDeclaredField(name); return true; } catch (NoSuchFieldException ignored) { /* keep walking */ }
            }
            return false;
        }
    }
}
