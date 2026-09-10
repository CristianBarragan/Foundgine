package com.foundgine.core.semantic.metadata;
import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
/** Provider-neutral reference to a physical column. */
public record ColumnReference(EntityId entityId, ColumnId columnId) {}
