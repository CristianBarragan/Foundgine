package com.foundgine.core.semantic.query;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.SemanticValue;

public record SemanticAggregateFilter(RelationshipId relationship, SemanticFilterAggregate aggregate, FieldId field,
		SemanticAggregateFilterOperator operator, Object value, SemanticFilterExpression predicate)
		implements SemanticFilterExpression {
	public SemanticAggregateFilter(RelationshipId r, SemanticFilterAggregate a, FieldId f,
			SemanticAggregateFilterOperator o, Object v) {
		this(r, a, f, o, v, null);
	}

	public SemanticValue semanticValue() {
		return SemanticValue.from(value);
	}
}
