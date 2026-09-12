package com.foundgine.core.semantic.aggregates;

import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import java.util.*;

/** Fail-closed semantic legality boundary for aggregate substitution. */
public final class AggregateRewriteLegality {
	private AggregateRewriteLegality() {
	}

	public static AggregateRewriteLegalityResult checkEmptySemantics(SemanticAggregateSemantics from,
			SemanticAggregateSemantics to) {
		Objects.requireNonNull(from);
		Objects.requireNonNull(to);
		return from.emptyCollectionResult() == to.emptyCollectionResult() ? AggregateRewriteLegalityResult.LEGAL
				: AggregateRewriteLegalityResult.illegal("empty-collection semantics differ: '" + from.aggregate()
						+ "' yields " + describe(from.emptyCollectionResult()) + " for an empty collection, but '"
						+ to.aggregate() + "' yields " + describe(to.emptyCollectionResult()) + ".");
	}

	public static AggregateRewriteLegalityResult checkNullSemantics(SemanticAggregateSemantics from,
			SemanticAggregateSemantics to) {
		Objects.requireNonNull(from);
		Objects.requireNonNull(to);
		return from.nullInputBehavior() == to.nullInputBehavior() ? AggregateRewriteLegalityResult.LEGAL
				: AggregateRewriteLegalityResult.illegal("NULL-input semantics differ: '" + from.aggregate() + "' is "
						+ describe(from.nullInputBehavior()) + ", but '" + to.aggregate() + "' is "
						+ describe(to.nullInputBehavior()) + ".");
	}

	public static AggregateRewriteLegalityResult checkDuplicateSensitivity(SemanticAggregateSemantics from,
			SemanticAggregateSemantics to) {
		Objects.requireNonNull(from);
		Objects.requireNonNull(to);
		return from.isDuplicateSensitive() == to.isDuplicateSensitive() ? AggregateRewriteLegalityResult.LEGAL
				: AggregateRewriteLegalityResult.illegal("duplicate sensitivity differs: '" + from.aggregate() + "' is "
						+ (from.isDuplicateSensitive() ? "duplicate-sensitive" : "duplicate-insensitive") + ", but '"
						+ to.aggregate() + "' is "
						+ (to.isDuplicateSensitive() ? "duplicate-sensitive" : "duplicate-insensitive") + ".");
	}

	public static AggregateRewriteLegalityResult checkCardinalityRequirement(SemanticAggregateSemantics from,
			SemanticAggregateSemantics to, SemanticCardinalityKnowledge knowledge) {
		Objects.requireNonNull(from);
		Objects.requireNonNull(to);
		Objects.requireNonNull(knowledge);
		if (!from.requiresCardinalityProof() && !to.requiresCardinalityProof())
			return AggregateRewriteLegalityResult.LEGAL;
		return knowledge != SemanticCardinalityKnowledge.UNKNOWN ? AggregateRewriteLegalityResult.LEGAL
				: AggregateRewriteLegalityResult
						.illegal("cardinality proof is required to substitute '" + to.aggregate() + "' for '"
								+ from.aggregate() + "', but the relationship cardinality is unknown at rewrite time.");
	}

	public static AggregateRewriteLegalityResult checkSubstitution(SemanticAggregateSemantics from,
			SemanticAggregateSemantics to, SemanticCardinalityKnowledge knowledge) {
		Objects.requireNonNull(from);
		Objects.requireNonNull(to);
		Objects.requireNonNull(knowledge);
		if (from.aggregate() == to.aggregate())
			return AggregateRewriteLegalityResult.LEGAL;
		return AggregateRewriteLegalityResult.combine(checkEmptySemantics(from, to), checkNullSemantics(from, to),
				checkDuplicateSensitivity(from, to), checkCardinalityRequirement(from, to, knowledge));
	}

	public static AggregateRewriteLegalityResult checkSubstitution(SemanticFilterAggregate from,
			SemanticFilterAggregate to, SemanticCardinalityKnowledge knowledge) {
		return checkSubstitution(SemanticAggregateSemanticsCatalog.forAggregate(from),
				SemanticAggregateSemanticsCatalog.forAggregate(to), knowledge);
	}

	public static AggregateRewriteLegalityResult checkSubstitution(SemanticFilterAggregate from,
			SemanticFilterAggregate to) {
		return checkSubstitution(from, to, SemanticCardinalityKnowledge.UNKNOWN);
	}

	private static String describe(SemanticEmptyCollectionResult x) {
		return x == SemanticEmptyCollectionResult.ZERO ? "zero" : "NULL";
	}

	private static String describe(SemanticNullInputBehavior x) {
		return x == SemanticNullInputBehavior.NEVER_NULL ? "never NULL" : "NULL-ignoring";
	}
}
