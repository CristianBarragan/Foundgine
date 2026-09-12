package com.foundgine.core.semantic.aggregates;

import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import java.util.*;

/** Machine-readable aggregate semantics catalog. */
public final class SemanticAggregateSemanticsCatalog {
	private SemanticAggregateSemanticsCatalog() {
	}

	public static final SemanticAggregateSemantics COUNT = new SemanticAggregateSemantics(SemanticFilterAggregate.COUNT,
			SemanticEmptyCollectionResult.ZERO, SemanticNullInputBehavior.NEVER_NULL,
			SemanticDuplicateSensitivity.SENSITIVE);
	public static final SemanticAggregateSemantics MIN = new SemanticAggregateSemantics(SemanticFilterAggregate.MIN,
			SemanticEmptyCollectionResult.NULL, SemanticNullInputBehavior.IGNORES_NULL,
			SemanticDuplicateSensitivity.INSENSITIVE);
	public static final SemanticAggregateSemantics MAX = new SemanticAggregateSemantics(SemanticFilterAggregate.MAX,
			SemanticEmptyCollectionResult.NULL, SemanticNullInputBehavior.IGNORES_NULL,
			SemanticDuplicateSensitivity.INSENSITIVE);
	private static final Map<SemanticFilterAggregate, SemanticAggregateSemantics> BY_AGGREGATE = Map.of(
			SemanticFilterAggregate.COUNT, COUNT, SemanticFilterAggregate.MIN, MIN, SemanticFilterAggregate.MAX, MAX);
	public static final List<SemanticAggregateSemantics> ALL = List.of(COUNT, MIN, MAX);

	public static SemanticAggregateSemantics forAggregate(SemanticFilterAggregate aggregate) {
		var result = aggregate == null ? null : BY_AGGREGATE.get(aggregate);
		if (result == null)
			throw new UnsupportedOperationException("No semantic contract is registered for aggregate '" + aggregate
					+ "'. Register one in SemanticAggregateSemanticsCatalog before using it in a rewrite rule.");
		return result;
	}

	public static Optional<SemanticAggregateSemantics> tryGet(SemanticFilterAggregate aggregate) {
		return aggregate == null ? Optional.empty() : Optional.ofNullable(BY_AGGREGATE.get(aggregate));
	}
}
