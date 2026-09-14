package com.foundgine.providers.storage.inmemory;

import com.foundgine.core.abstractions.EntityId;

import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/** Mutable builder for deterministic entity-partitioned in-memory data. */
public final class InMemoryDataSet {
    private final Map<EntityId, List<InMemoryRow>> rows = new LinkedHashMap<>();

    public InMemoryDataSet add(InMemoryRow row) {
        Objects.requireNonNull(row, "row");
        rows.computeIfAbsent(row.entityId(), ignored -> new ArrayList<>()).add(row);
        return this;
    }

    public List<InMemoryRow> get(EntityId entityId) {
        var result = rows.get(entityId);
        return result == null ? List.of() : Collections.unmodifiableList(result);
    }
}
