package com.foundgine.core.semantic.planning;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** Port of {@code PlanOptimizationProofTests} (Foundgine.Planning.Tests). */
class PlanOptimizationProofParityTest {

	@Test
	void optimizationProofRequiresAllPreservationDimensions() {
		var proof = new PlanOptimizationProof("test-rule", /* semanticMeaningPreserved */ true,
				/* securityPreserved */ true, /* authorizationBindingPreserved */ true, /* estimatedBenefit */ 1d,
				/* estimatedRewriteCost */ 0.5d);

		assertTrue(proof.isSatisfied());
	}

	@Test
	void optimizationProofRejectsSemanticLoss() {
		var proof = new PlanOptimizationProof("test-rule", /* semanticMeaningPreserved */ false,
				/* securityPreserved */ true, /* authorizationBindingPreserved */ true, /* estimatedBenefit */ 10d,
				/* estimatedRewriteCost */ 0d);

		assertFalse(proof.isSatisfied());
	}

	@Test
	void optimizationProofRejectsAuthorizationBindingLoss() {
		var proof = new PlanOptimizationProof("test-rule", /* semanticMeaningPreserved */ true,
				/* securityPreserved */ true, /* authorizationBindingPreserved */ false, /* estimatedBenefit */ 10d,
				/* estimatedRewriteCost */ 0d);

		assertFalse(proof.isSatisfied());
	}
}
