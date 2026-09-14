package com.foundgine.core.semantic.planning.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import java.util.*;

public record MutationIntent(EntityId entity, MutationKind kind, List<MutationFieldValue> fields,
		SemanticFilterExpression filter, List<FieldId> returnFields) implements IMutationIntent {
	public MutationIntent {
		Objects.requireNonNull(entity);
		Objects.requireNonNull(kind);
		fields = fields == null ? List.of() : List.copyOf(fields);
		returnFields = returnFields == null ? null : List.copyOf(returnFields);
	}

	public MutationIntent(EntityId entity, MutationKind kind, List<MutationFieldValue> fields) {
		this(entity, kind, fields, null, null);
	}

	public MutationIntent(EntityId entity, MutationKind kind, List<MutationFieldValue> fields,
			SemanticFilterExpression filter) {
		this(entity, kind, fields, filter, null);
	}
}
