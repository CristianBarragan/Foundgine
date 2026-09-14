package com.foundgine.core.semantic.mutation;

import com.foundgine.core.abstractions.FieldId;

/**
 * Semantic field assignment; physical column mapping is outside this
 * representation.
 */
public record SemanticMutationField(FieldId field, Object value, SemanticMutationValueReference source) {
	public SemanticMutationField {
		if (field == null)
			throw new NullPointerException("field");
	}

	public SemanticMutationField(FieldId field, Object value) {
		this(field, value, null);
	}
}
