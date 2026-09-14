package com.foundgine.core.semantic.intent;

import com.foundgine.core.semantic.query.SemanticRelationshipQuantifier;

public record ReadRelationshipFilter(String relationship, SemanticRelationshipQuantifier quantifier,
		ReadFilter predicate) implements ReadFilter {
}
