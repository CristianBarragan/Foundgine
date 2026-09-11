package com.foundgine.core.semantic.query;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.expressions.SemanticExpression;
public interface SemanticFilterExpression extends SemanticExpression { @Override default SemanticType resultType(){return new SemanticType.Scalar(SemanticScalarKind.BOOLEAN);} }
