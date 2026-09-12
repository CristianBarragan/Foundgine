package com.foundgine.core.semantic.aggregates;

import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import java.util.*;

/** Central semantic contract for an aggregate function. */
public record SemanticAggregateSemantics(SemanticFilterAggregate aggregate,
		SemanticEmptyCollectionResult emptyCollectionResult, SemanticNullInputBehavior nullInputBehavior,
		SemanticDuplicateSensitivity duplicateSensitivity, SemanticCardinalityRequirement cardinalityRequirement) {
	public SemanticAggregateSemantics {
		Objects.requireNonNull(aggregate);
		Objects.requireNonNull(emptyCollectionResult);
		Objects.requireNonNull(nullInputBehavior);
		Objects.requireNonNull(duplicateSensitivity);
		cardinalityRequirement = cardinalityRequirement == null ? SemanticCardinalityRequirement.NONE
				: cardinalityRequirement;
	}

	public SemanticAggregateSemantics(SemanticFilterAggregate aggregate, SemanticEmptyCollectionResult empty,
			SemanticNullInputBehavior nulls, SemanticDuplicateSensitivity duplicates) {
		this(aggregate, empty, nulls, duplicates, SemanticCardinalityRequirement.NONE);
	}

	public boolean isDuplicateSensitive() {
		return duplicateSensitivity == SemanticDuplicateSensitivity.SENSITIVE;
	}

	public boolean requiresCardinalityProof() {
		return cardinalityRequirement == SemanticCardinalityRequirement.REQUIRES_PROOF;
	}
}
