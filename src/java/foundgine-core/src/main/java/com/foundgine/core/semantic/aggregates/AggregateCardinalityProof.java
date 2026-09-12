package com.foundgine.core.semantic.aggregates;

import com.foundgine.core.semantic.RelationshipCardinality;
import java.util.Objects;

/**
 * Bridges structurally-proven relationship cardinality into rewrite-proof
 * knowledge.
 */
public record AggregateCardinalityProof(SemanticCardinalityKnowledge knowledge) {
	public static final AggregateCardinalityProof UNKNOWN = new AggregateCardinalityProof(
			SemanticCardinalityKnowledge.UNKNOWN);

	public static AggregateCardinalityProof fromCardinality(RelationshipCardinality cardinality) {
		Objects.requireNonNull(cardinality, "cardinality");
		return new AggregateCardinalityProof(
				cardinality == RelationshipCardinality.ONE ? SemanticCardinalityKnowledge.AT_MOST_ONE
						: SemanticCardinalityKnowledge.UNBOUNDED);
	}
}
