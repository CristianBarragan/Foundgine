package com.foundgine.providers.storage.orm;

import java.util.ArrayList;
import java.util.IdentityHashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Reference {@link IOrmMutationSource} over plain entity objects with real rollback (snapshot of
 * membership and attribute values). Assigns Long/Integer keys on persist when the key is null,
 * mimicking database-generated identities.
 */
public final class InMemoryOrmMutationSource implements IOrmMutationSource {
    private final OrmRegistry registry;
    private final Map<Class<?>, List<Object>> data = new LinkedHashMap<>();
    private long nextKey = 1000;
    private boolean active;

    public InMemoryOrmMutationSource(OrmRegistry registry) {
        this.registry = registry;
    }

    public List<Object> all(Class<?> type) {
        return List.copyOf(data.getOrDefault(type, List.of()));
    }

    public void seed(Object entity) {
        data.computeIfAbsent(entity.getClass(), k -> new ArrayList<>()).add(entity);
    }

    @Override
    public Transaction begin() {
        if (active) throw new IllegalStateException("A transaction is already active.");
        active = true;
        var members = new LinkedHashMap<Class<?>, List<Object>>();
        data.forEach((k, v) -> members.put(k, new ArrayList<>(v)));
        var values = new IdentityHashMap<Object, Map<String, Object>>();
        for (var map : registry.all()) {
            for (Object e : data.getOrDefault(map.type(), List.of())) {
                var snap = new LinkedHashMap<String, Object>();
                for (var b : map.fields()) snap.put(b.name(), OrmEntityAccess.get(e, b.name()));
                values.put(e, snap);
            }
        }
        return new Transaction() {
            boolean done;

            @Override
            public void commit() {
                done = true;
                active = false;
            }

            @Override
            public void rollback() {
                if (done) return;
                done = true;
                active = false;
                data.clear();
                data.putAll(members);
                values.forEach((e, snap) -> snap.forEach((n, v) -> {
                    if (OrmEntityAccess.canSet(e.getClass(), n)) OrmEntityAccess.set(e, n, v);
                }));
            }

            @Override
            public void close() {
                if (!done) rollback();
            }
        };
    }

    @Override
    public List<Object> find(OrmEntityMapping map, OrmPredicate where) {
        var result = new ArrayList<Object>();
        for (Object e : data.getOrDefault(map.type(), List.of())) {
            if (InMemoryOrmQuerySource.evaluate(where,
                    f -> OrmEntityAccess.get(e, map.field(f).name()), related -> {
                        var rows = new ArrayList<Map<com.foundgine.core.abstractions.FieldId, Object>>();
                        for (Object child : data.getOrDefault(related.type(), List.of())) {
                            var row = new LinkedHashMap<com.foundgine.core.abstractions.FieldId, Object>();
                            for (var field : related.fields()) row.put(field.id(), OrmEntityAccess.get(child, field.name()));
                            rows.add(row);
                        }
                        return rows;
                    })) {
                result.add(e);
            }
        }
        return result;
    }

    @Override
    public void persist(OrmEntityMapping map, Object entity) {
        for (var pk : map.primaryKeys()) {
            if (OrmEntityAccess.get(entity, pk.name()) == null && Number.class.isAssignableFrom(pk.type())) {
                OrmEntityAccess.set(entity, pk.name(), OrmValues.coerce(nextKey++, pk.type()));
            }
        }
        data.computeIfAbsent(map.type(), k -> new ArrayList<>()).add(entity);
    }

    @Override
    public void remove(OrmEntityMapping map, Object entity) {
        data.getOrDefault(map.type(), new ArrayList<>()).remove(entity);
    }

    @Override
    public void flush() {
        // nothing is buffered
    }
}
