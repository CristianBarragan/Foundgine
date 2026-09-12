package com.foundgine.core.semantic.mutation;

import com.foundgine.core.abstractions.FieldId;

/** References a semantic value produced by an earlier mutation operation. */
public record SemanticMutationValueReference(int sourceOperationIndex, FieldId sourceField) {
	public SemanticMutationValueReference {
		if (sourceOperationIndex < 0)
			throw new IllegalArgumentException("sourceOperationIndex must be non-negative.");
	}
}
