package com.foundgine.core.semantic.planning.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import java.util.*;

public record MutationOperation(MutationEntitySchema entity, MutationKind kind, List<MutationFieldValue> fields,
		SemanticFilterExpression filter, List<ColumnId> conflictColumns, List<FieldId> returnFields) {
	public MutationOperation {
		Objects.requireNonNull(entity);
		Objects.requireNonNull(kind);
		fields = fields == null ? List.of() : List.copyOf(fields);
		conflictColumns = conflictColumns == null ? null : List.copyOf(conflictColumns);
		returnFields = returnFields == null ? null : List.copyOf(returnFields);
	}

	public MutationOperation(MutationEntitySchema e, MutationKind k, List<MutationFieldValue> f,
			SemanticFilterExpression filter) {
		this(e, k, f, filter, null, null);
	}
}
