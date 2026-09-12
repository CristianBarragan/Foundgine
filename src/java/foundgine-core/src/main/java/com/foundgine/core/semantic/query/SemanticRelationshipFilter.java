package com.foundgine.core.semantic.query;

import com.foundgine.core.abstractions.RelationshipId;

public record SemanticRelationshipFilter(RelationshipId relationship, SemanticRelationshipQuantifier quantifier,
		SemanticFilterExpression predicate) implements SemanticFilterExpression {
}
