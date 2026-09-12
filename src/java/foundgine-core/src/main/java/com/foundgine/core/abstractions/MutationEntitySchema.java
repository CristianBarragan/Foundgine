package com.foundgine.core.abstractions;

import java.util.Map;
import java.util.Set;

/**
 * Port of {@code Foundgine.Core.Abstractions.MutationEntitySchema}.
 *
 * <p>
 * {@code fields} maps a field to its backing column; a {@code null} value
 * mirrors the C# {@code ColumnId?} (a field with no direct column backing).
 * {@code primaryKeyColumn} is likewise nullable.
 */
public record MutationEntitySchema(EntityId id, String name, Set<ColumnId> columns, Map<FieldId, ColumnId> fields,
		ColumnId primaryKeyColumn) {
}
