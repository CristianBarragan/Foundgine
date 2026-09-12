package com.foundgine.core.semantic.query;

import java.util.*;

public record SemanticQueryOptions(SemanticFilterExpression filter, List<SemanticOrderTerm> order, Integer limit,
		Integer offset, String after) {
	public SemanticQueryOptions {
		order = order == null ? List.of() : List.copyOf(order);
	}

	public SemanticQueryOptions() {
		this(null, List.of(), null, null, null);
	}

	public List<SemanticOrderTerm> effectiveOrder() {
		return order;
	}

	public boolean hasCursor() {
		return after != null && !after.isBlank();
	}
}
