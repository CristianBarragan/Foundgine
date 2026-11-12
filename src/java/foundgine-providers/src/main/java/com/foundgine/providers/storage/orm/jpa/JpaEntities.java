package com.foundgine.providers.storage.orm.jpa;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.execution.ProviderPlan;
import com.foundgine.providers.storage.orm.OrmEntityMapping;
import com.foundgine.providers.storage.orm.OrmFieldBinding;
import com.foundgine.providers.storage.orm.OrmRegistry;
import com.foundgine.providers.storage.orm.OrmValues;

import jakarta.persistence.EntityManager;

import java.lang.reflect.Field;
import java.util.ArrayList;
import java.util.Collection;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/**
 * Name-addressed, fully generic access to every mapped entity: the same CRUD and semantic read for any entity,
 * with no per-entity code (the Java counterpart of the .NET {@code FoundgineEntities}). Mechanical like the
 * mutation source: it holds no authorization for writes, so authorize the capability before calling. Reads are
 * different: they only run through the {@link ReadGate}, which must return an authorized provider plan.
 *
 * <p>One instance wraps one {@link EntityManager} and is not thread-safe. Writes join an already active
 * transaction or own one (see {@link JpaOrmMutationSource}). The compatibility constructor uses reflective
 * access; the generated-model constructor uses direct factories/accessors only. Composite keys are not supported
 * by the by-key operations.
 *
 * <p>UNVERIFIED: written without a Java compiler or the JPA jars available.
 */
public final class JpaEntities {
    /** An authorized, provider-lowered read and the execution context it must run with. */
    public record GatedRead(ProviderPlan plan, ExecutionContext context) {
        public GatedRead {
            Objects.requireNonNull(plan, "plan");
            Objects.requireNonNull(context, "context");
        }
    }

    /**
     * Turns a read request into an authorized plan (semantic operation -> authorization -> plan -> ExecutionIR ->
     * provider plan). The gateway never builds a read plan itself, so a read cannot bypass authorization.
     */
    @FunctionalInterface
    public interface ReadGate {
        GatedRead authorize(OrmEntityMapping entity, List<FieldId> fields, Map<FieldId, Object> equals);
    }

    private final EntityManager em;
    private final OrmRegistry registry;
    private final ReadGate readGate;
    private final Map<Class<?>, JpaEntityModel<?>> generatedModels;
    private final boolean requireGeneratedModels;

    public JpaEntities(EntityManager em, OrmRegistry registry, ReadGate readGate) {
        this(em, registry, readGate, List.of(), false);
    }

    /** Creates a strictly AOT-safe gateway. Every entity must have a generated direct-access model. */
    public JpaEntities(EntityManager em, OrmRegistry registry, ReadGate readGate,
            Collection<JpaEntityModel<?>> generatedModels) {
        this(em, registry, readGate, generatedModels, true);
    }

    private JpaEntities(EntityManager em, OrmRegistry registry, ReadGate readGate,
            Collection<JpaEntityModel<?>> models, boolean requireGeneratedModels) {
        this.em = Objects.requireNonNull(em, "em");
        this.registry = Objects.requireNonNull(registry, "registry");
        this.readGate = Objects.requireNonNull(readGate, "readGate");
        var index = new LinkedHashMap<Class<?>, JpaEntityModel<?>>();
        for (var model : Objects.requireNonNull(models, "generatedModels")) {
            if (index.put(model.type(), model) != null)
                throw new IllegalArgumentException("Duplicate generated JPA model for " + model.type().getName());
        }
        this.generatedModels = Map.copyOf(index);
        this.requireGeneratedModels = requireGeneratedModels;
        if (requireGeneratedModels) {
            for (var mapping : registry.all()) {
                if (!this.generatedModels.containsKey(mapping.type()))
                    throw new IllegalArgumentException("Missing generated JPA model for " + mapping.type().getName());
            }
        }
    }

    public List<String> names() {
        return registry.all().stream().map(OrmEntityMapping::name).toList();
    }

    public boolean has(String name) {
        return find(name) != null;
    }

    public Entity entity(String name) {
        var mapping = find(name);
        if (mapping == null) throw new IllegalArgumentException("Unknown entity '" + name + "'.");
        return new Entity(mapping);
    }

    private OrmEntityMapping find(String name) {
        for (var mapping : registry.all()) if (mapping.name().equalsIgnoreCase(name)) return mapping;
        return null;
    }

    /** One entity, addressed by name at run time. */
    public final class Entity {
        private final OrmEntityMapping mapping;

        private Entity(OrmEntityMapping mapping) {
            this.mapping = mapping;
        }

        public String name() {
            return mapping.name();
        }

        public Class<?> type() {
            return mapping.type();
        }

        public List<String> fields() {
            return mapping.fields().stream().map(OrmFieldBinding::name).toList();
        }

        /** Semantic read (authorized plan -> JPA). {@code equals} are attribute = value filters. */
        public List<Map<String, Object>> read(Map<String, Object> equals, List<String> fields) {
            var selected = new ArrayList<OrmFieldBinding>();
            if (fields == null || fields.isEmpty()) selected.addAll(mapping.fields());
            else for (var f : fields) selected.add(binding(f));

            var filters = new LinkedHashMap<FieldId, Object>();
            if (equals != null) {
                for (var kv : equals.entrySet()) {
                    var b = binding(kv.getKey());
                    filters.put(b.id(), OrmValues.coerce(kv.getValue(), b.type()));
                }
            }

            var gated = readGate.authorize(mapping, selected.stream().map(OrmFieldBinding::id).toList(), filters);
            List<ExecutionRow> rows = JpaOrmProvider.execution(registry, em)
                    .execute(gated.plan(), gated.context(), CancellationToken.NONE)
                    .rows();
            var out = new ArrayList<Map<String, Object>>(rows.size());
            for (var row : rows) {
                var values = new LinkedHashMap<String, Object>();
                for (var b : selected) values.put(b.name(), row.values().get(b.name()));
                out.add(values);
            }
            return out;
        }

        public Object get(Object id) {
            return em.find(mapping.type(), key(id));
        }

        /** Creates a row from attribute values; unknown attributes are rejected. Returns the persisted entity. */
        public Object create(Map<String, Object> values) {
            Objects.requireNonNull(values, "values");
            Object entity;
            var generated = generatedModels.get(mapping.type());
            if (generated != null) entity = generated.create();
            else if (requireGeneratedModels) throw new IllegalStateException("Missing generated JPA model.");
            else {
                try {
                    var ctor = mapping.type().getDeclaredConstructor();
                    ctor.setAccessible(true);
                    entity = ctor.newInstance();
                } catch (ReflectiveOperationException e) {
                    throw new IllegalStateException(
                            mapping.type().getSimpleName() + " needs a no-arg constructor to be created generically.", e);
                }
            }
            for (var kv : values.entrySet()) set(entity, binding(kv.getKey()), kv.getValue());
            try (var tx = new JpaOrmMutationSource(em).begin()) {
                em.persist(entity);
                em.flush();
                tx.commit();
            }
            return entity;
        }

        /** Sets attributes of the row with this key. Returns rows affected (0 when the key is unknown). */
        public int update(Object id, Map<String, Object> values) {
            Objects.requireNonNull(values, "values");
            for (var name : values.keySet()) {
                if (binding(name).primaryKey()) {
                    throw new IllegalArgumentException("The key field '" + name + "' cannot be updated.");
                }
            }
            try (var tx = new JpaOrmMutationSource(em).begin()) {
                var entity = em.find(mapping.type(), key(id));
                if (entity == null) return 0;
                for (var kv : values.entrySet()) set(entity, binding(kv.getKey()), kv.getValue());
                em.flush();
                tx.commit();
                return 1;
            }
        }

        /** Deletes the row with this key. Returns rows affected. */
        public int delete(Object id) {
            try (var tx = new JpaOrmMutationSource(em).begin()) {
                var entity = em.find(mapping.type(), key(id));
                if (entity == null) return 0;
                em.remove(entity);
                em.flush();
                tx.commit();
                return 1;
            }
        }

        private OrmFieldBinding binding(String name) {
            for (var b : mapping.fields()) if (b.name().equalsIgnoreCase(name)) return b;
            throw new IllegalArgumentException("Entity '" + mapping.name() + "' has no field '" + name + "'.");
        }

        private Object key(Object id) {
            var keys = mapping.primaryKeys();
            if (keys.size() != 1) {
                throw new IllegalStateException(
                        "Entity '" + mapping.name() + "' needs exactly one primary key for by-key access.");
            }
            return Objects.requireNonNull(OrmValues.coerce(id, keys.get(0).type()), "id");
        }

        private void set(Object entity, OrmFieldBinding binding, Object value) {
            var generated = generatedModels.get(mapping.type());
            if (generated != null) {
                if (!generated.canWrite(binding.name()))
                    throw new IllegalArgumentException("Attribute '" + binding.name() + "' is not writable in the generated JPA model.");
                writeGenerated(generated, entity, binding, value);
                return;
            }
            if (requireGeneratedModels) throw new IllegalStateException("Missing generated JPA model.");
            Field field = declaredField(entity.getClass(), binding.name());
            try {
                field.setAccessible(true);
                field.set(entity, OrmValues.coerce(value, binding.type()));
            } catch (IllegalAccessException e) {
                throw new IllegalStateException("Cannot set " + binding.name() + ".", e);
            }
        }

        @SuppressWarnings({"rawtypes", "unchecked"})
        private void writeGenerated(JpaEntityModel model, Object entity, OrmFieldBinding binding, Object value) {
            model.write(entity, binding.name(), OrmValues.coerce(value, binding.type()));
        }

        private Field declaredField(Class<?> type, String name) {
            for (Class<?> c = type; c != null; c = c.getSuperclass()) {
                try {
                    return c.getDeclaredField(name);
                } catch (NoSuchFieldException ignored) {
                    // keep walking up
                }
            }
            throw new IllegalStateException(type.getSimpleName() + " does not declare attribute '" + name + "'.");
        }
    }
}
