package com.foundgine.core.semantic.query;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.SemanticValue;

public record SemanticFieldFilter(FieldId field, SemanticFilterOperator operator, Object value)
		implements SemanticFilterExpression {
	public SemanticValue semanticValue() {
		return SemanticValue.from(value);
	}
}
