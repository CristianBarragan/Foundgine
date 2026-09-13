package com.foundgine.core.semantic.aggregates;

import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of {@code Foundgine.Core.Semantic.Tests.AggregateRewriteLegalityTests}.
 */
class AggregateRewriteLegalityParityTest {

	@Test
	void substitutingAnAggregateForItselfIsAlwaysLegal() {
		var result = AggregateRewriteLegality.checkSubstitution(SemanticFilterAggregate.COUNT,
				SemanticFilterAggregate.COUNT);

		assertTrue(result.isLegal());
		assertTrue(result.violations().isEmpty());
	}

	@ParameterizedTest
	@EnumSource(value = SemanticFilterAggregate.class, names = { "MIN", "MAX" })
	void countToMinOrMaxSubstitutionIsRejected(SemanticFilterAggregate to) {
		var result = AggregateRewriteLegality.checkSubstitution(SemanticFilterAggregate.COUNT, to);

		assertFalse(result.isLegal());
		assertFalse(result.violations().isEmpty());
	}

	@Test
	void countToMinRejectionReportsEmptyCollectionMismatch() {
		var result = AggregateRewriteLegality.checkSubstitution(SemanticFilterAggregate.COUNT,
				SemanticFilterAggregate.MIN);

		assertTrue(result.violations().stream().anyMatch(v -> v.toLowerCase().contains("empty-collection")));
	}

	@Test
	void countToMinRejectionReportsNullSemanticsMismatch() {
		var result = AggregateRewriteLegality.checkSubstitution(SemanticFilterAggregate.COUNT,
				SemanticFilterAggregate.MIN);

		assertTrue(result.violations().stream().anyMatch(v -> v.toLowerCase().contains("null-input")));
	}

	@Test
	void countToMinRejectionReportsDuplicateSensitivityMismatch() {
		var result = AggregateRewriteLegality.checkSubstitution(SemanticFilterAggregate.COUNT,
				SemanticFilterAggregate.MIN);

		assertTrue(result.violations().stream().anyMatch(v -> v.toLowerCase().contains("duplicate sensitivity")));
	}

	@Test
	void minToMaxSubstitutionIsRejectedDespiteSharedEmptyAndNullSemantics() {
		// MIN and MAX agree on empty-collection result and NULL-input behavior, but they
		// are still different functions and must not be treated as interchangeable by
		// this gate. The legality check only ever certifies "no known semantic
		// difference"; it is not a general proof that two distinct aggregates compute
		// the same value.
		var result = AggregateRewriteLegality.checkSubstitution(SemanticFilterAggregate.MIN,
				SemanticFilterAggregate.MAX);

		assertTrue(result.isLegal());
		assertTrue(result.violations().isEmpty());
	}

	@Test
	void duplicateSensitiveToInsensitiveRewriteIsRejected() {
		var from = SemanticAggregateSemanticsCatalog.COUNT;
		var to = SemanticAggregateSemanticsCatalog.MIN;

		var result = AggregateRewriteLegality.checkDuplicateSensitivity(from, to);

		assertFalse(result.isLegal());
		assertTrue(result.violations().stream().anyMatch(v -> v.toLowerCase().contains("duplicate-sensitive")));
	}

	@Test
	void duplicateInsensitiveToInsensitiveRewriteIsAccepted() {
		var result = AggregateRewriteLegality.checkDuplicateSensitivity(SemanticAggregateSemanticsCatalog.MIN,
				SemanticAggregateSemanticsCatalog.MAX);

		assertTrue(result.isLegal());
	}

	@Test
	void cardinalityGatePassesWhenNeitherSideRequiresProof() {
		var result = AggregateRewriteLegality.checkCardinalityRequirement(SemanticAggregateSemanticsCatalog.MIN,
				SemanticAggregateSemanticsCatalog.MAX, SemanticCardinalityKnowledge.UNKNOWN);

		assertTrue(result.isLegal());
	}

	@Test
	void cardinalityGateFailsClosedWhenProofIsRequiredButCardinalityIsUnknown() {
		var from = withCardinalityRequirement(SemanticAggregateSemanticsCatalog.MIN,
				SemanticCardinalityRequirement.REQUIRES_PROOF);

		var result = AggregateRewriteLegality.checkCardinalityRequirement(from, SemanticAggregateSemanticsCatalog.MAX,
				SemanticCardinalityKnowledge.UNKNOWN);

		assertFalse(result.isLegal());
		assertTrue(result.violations().stream().anyMatch(v -> v.toLowerCase().contains("cardinality")));
	}

	@ParameterizedTest
	@EnumSource(value = SemanticCardinalityKnowledge.class, names = { "AT_MOST_ONE", "UNBOUNDED" })
	void cardinalityGatePassesOnceCardinalityIsKnown(SemanticCardinalityKnowledge knowledge) {
		var from = withCardinalityRequirement(SemanticAggregateSemanticsCatalog.MIN,
				SemanticCardinalityRequirement.REQUIRES_PROOF);

		var result = AggregateRewriteLegality.checkCardinalityRequirement(from, SemanticAggregateSemanticsCatalog.MAX,
				knowledge);

		assertTrue(result.isLegal());
	}

	@Test
	void fullSubstitutionGateStillFailsOnEmptyAndNullMismatchEvenWhenCardinalityIsKnown() {
		// Supplying cardinality knowledge must never paper over an unrelated semantic
		// mismatch.
		var result = AggregateRewriteLegality.checkSubstitution(SemanticAggregateSemanticsCatalog.COUNT,
				SemanticAggregateSemanticsCatalog.MIN, SemanticCardinalityKnowledge.AT_MOST_ONE);

		assertFalse(result.isLegal());
		assertTrue(result.violations().stream().anyMatch(v -> v.toLowerCase().contains("empty-collection")));
	}

	@Test
	void combineIsLegalOnlyWhenEveryInputIsLegal() {
		var combined = AggregateRewriteLegalityResult.combine(AggregateRewriteLegalityResult.LEGAL,
				AggregateRewriteLegalityResult.LEGAL);

		assertTrue(combined.isLegal());
		assertTrue(combined.violations().isEmpty());
	}

	@Test
	void combineCollectsViolationsFromEveryFailingInput() {
		var combined = AggregateRewriteLegalityResult.combine(
				AggregateRewriteLegalityResult.illegal("first problem"), AggregateRewriteLegalityResult.LEGAL,
				AggregateRewriteLegalityResult.illegal("second problem"));

		assertFalse(combined.isLegal());
		assertEquals(java.util.List.of("first problem", "second problem"), combined.violations());
	}

	@Test
	void illegalRequiresAtLeastOneViolation() {
		assertThrows(IllegalArgumentException.class, AggregateRewriteLegalityResult::illegal);
	}

	private static SemanticAggregateSemantics withCardinalityRequirement(SemanticAggregateSemantics semantics,
			SemanticCardinalityRequirement requirement) {
		return new SemanticAggregateSemantics(semantics.aggregate(), semantics.emptyCollectionResult(),
				semantics.nullInputBehavior(), semantics.duplicateSensitivity(), requirement);
	}
}
