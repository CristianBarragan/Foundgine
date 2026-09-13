package com.foundgine.core.semantic.planning.mutation;

import com.foundgine.core.abstractions.*;
import java.util.*;

public record UpsertIntent(EntityId entity, List<MutationFieldValue> fields, List<ColumnId> conflictColumns,
		List<FieldId> returnFields) implements IMutationIntent {
	public UpsertIntent {
		Objects.requireNonNull(entity);
		fields = fields == null ? List.of() : List.copyOf(fields);
		conflictColumns = conflictColumns == null ? null : List.copyOf(conflictColumns);
		returnFields = returnFields == null ? null : List.copyOf(returnFields);
	}

	public MutationKind kind() {
		return MutationKind.UPSERT;
	}

	public UpsertIntent(EntityId entity, List<MutationFieldValue> fields) {
		this(entity, fields, null, null);
	}
}
