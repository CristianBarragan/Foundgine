package com.foundgine.core.semantic.query;

import java.util.*;

public record SemanticAndFilter(List<SemanticFilterExpression> expressions) implements SemanticFilterExpression {
	public SemanticAndFilter {
		expressions = List.copyOf(expressions);
	}
}
