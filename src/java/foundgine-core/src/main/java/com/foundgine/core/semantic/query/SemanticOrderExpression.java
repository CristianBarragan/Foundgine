package com.foundgine.core.semantic.query;

import com.foundgine.core.semantic.expressions.SemanticExpression;

public record SemanticOrderExpression(SemanticExpression expression, SemanticSortDirection direction) {
}
