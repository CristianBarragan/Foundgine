package com.foundgine.core.semantic.query;
import com.foundgine.core.abstractions.FieldId; import com.foundgine.core.semantic.SemanticType; import com.foundgine.core.semantic.expressions.SemanticExpression;
public record SemanticFieldOrderExpression(FieldId field,SemanticType type,SemanticSortDirection direction) { public SemanticExpression expression(){return new SemanticExpression.FieldReference(field,type);} }
