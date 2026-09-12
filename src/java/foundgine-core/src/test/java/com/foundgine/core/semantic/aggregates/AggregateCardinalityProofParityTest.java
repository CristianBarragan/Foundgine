package com.foundgine.core.semantic.aggregates;

import com.foundgine.core.semantic.RelationshipCardinality;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of {@code AggregateCardinalityProofTests} (Foundgine.Semantics.Tests).
 */
class AggregateCardinalityProofParityTest {

	@Test
	void fromCardinalityOneProvesAtMostOne() {
		var proof = AggregateCardinalityProof.fromCardinality(RelationshipCardinality.ONE);

		assertEquals(SemanticCardinalityKnowledge.AT_MOST_ONE, proof.knowledge());
	}

	@Test
	void fromCardinalityManyProvesUnbounded() {
		var proof = AggregateCardinalityProof.fromCardinality(RelationshipCardinality.MANY);

		assertEquals(SemanticCardinalityKnowledge.UNBOUNDED, proof.knowledge());
	}

	@Test
	void unknownCarriesNoCardinalityKnowledge() {
		assertEquals(SemanticCardinalityKnowledge.UNKNOWN, AggregateCardinalityProof.UNKNOWN.knowledge());
	}

	@Test
	void derivedKnowledgeSatisfiesTheLegalityGateWhenARequirementExists() {
		var min = SemanticAggregateSemanticsCatalog.MIN;
		var from = new SemanticAggregateSemantics(min.aggregate(), min.emptyCollectionResult(), min.nullInputBehavior(),
				min.duplicateSensitivity(), SemanticCardinalityRequirement.REQUIRES_PROOF);
		var proof = AggregateCardinalityProof.fromCardinality(RelationshipCardinality.ONE);

		var result = AggregateRewriteLegality.checkCardinalityRequirement(from, SemanticAggregateSemanticsCatalog.MAX,
				proof.knowledge());

		assertTrue(result.isLegal());
	}

	@Test
	void unknownStillFailsTheLegalityGateWhenARequirementExists() {
		var min = SemanticAggregateSemanticsCatalog.MIN;
		var from = new SemanticAggregateSemantics(min.aggregate(), min.emptyCollectionResult(), min.nullInputBehavior(),
				min.duplicateSensitivity(), SemanticCardinalityRequirement.REQUIRES_PROOF);

		var result = AggregateRewriteLegality.checkCardinalityRequirement(from, SemanticAggregateSemanticsCatalog.MAX,
				AggregateCardinalityProof.UNKNOWN.knowledge());

		assertFalse(result.isLegal());
	}
}
