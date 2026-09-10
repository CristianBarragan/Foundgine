package com.foundgine.core.semantic.query;
import com.foundgine.core.semantic.expressions.SemanticExpression;
public record SemanticAggregateOrderExpression(SemanticExpression source,SemanticExpression.AggregateExpressionKind aggregate,SemanticExpression argument,SemanticSortDirection direction) { public SemanticExpression expression(){return new SemanticExpression.Aggregate(aggregate,source,argument);} }
