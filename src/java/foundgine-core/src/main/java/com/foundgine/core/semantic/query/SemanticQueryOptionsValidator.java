package com.foundgine.core.semantic.query;

import com.foundgine.core.semantic.SemanticEntity;
import java.util.*;

/** Validates query controls before provider planning. */
public final class SemanticQueryOptionsValidator {
	private SemanticQueryOptionsValidator() {
	}

	public static void validate(SemanticQueryOptions options, SemanticEntity root) {
		Objects.requireNonNull(root);
		if (options == null)
			return;
		if (options.limit() != null && options.limit() < 0)
			throw invalid("Semantic query limit must be non-negative.");
		if (options.offset() != null && options.offset() < 0)
			throw invalid("Semantic query offset must be non-negative.");
		if (options.after() != null && options.after().isBlank())
			throw invalid("Semantic cursor cannot be empty.");
		if (options.after() != null && options.offset() != null)
			throw invalid("Cursor pagination cannot be combined with offset pagination.");
		if (options.after() != null && (options.limit() == null || options.limit() <= 0))
			throw invalid("Cursor pagination requires a positive limit.");
		var seen = new HashSet<String>();
		for (var term : options.effectiveOrder()) {
			var key = term.path().stream().map(Object::toString).reduce("", (a, b) -> a + (a.isEmpty() ? "" : "/") + b)
					+ ":" + term.field() + ":" + term.direction() + ":" + term.aggregate();
			if (!seen.add(key))
				throw invalid("Semantic query ordering contains a duplicate term.");
		}
	}

	private static IllegalStateException invalid(String m) {
		return new IllegalStateException(m);
	}
}
