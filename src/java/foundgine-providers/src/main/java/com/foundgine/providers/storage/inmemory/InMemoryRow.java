package com.foundgine.providers.storage.inmemory;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;

import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Objects;

/** One backing-store row used by the provider-neutral in-memory execution provider. */
public record InMemoryRow(EntityId entityId, Map<FieldId, Object> values) {
    public InMemoryRow {
        Objects.requireNonNull(entityId, "entityId");
        values = values == null ? Map.of() : java.util.Collections.unmodifiableMap(new LinkedHashMap<>(values));
    }
}
