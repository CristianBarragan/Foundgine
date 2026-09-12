package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.EntityId;
import org.junit.jupiter.api.Test;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

class GroundingDecisionTest {
	@Test
	void budgetExceededCannotCarryCommittedInterpretationByConstruction() {
		var decision = new GroundingDecision("orders", GroundingOutcome.BUDGET_EXCEEDED, null, List.of(),
				"search stopped", List.of(), GroundingBudgetLimit.MAX_PATHS_EXPLORED, List.of(), null, List.of());

		assertNull(decision.committed());
		assertEquals(GroundingBudgetLimit.MAX_PATHS_EXPLORED, decision.budgetLimit());
		assertFalse(decision.hadCompetingMeanings());
	}

	@Test
	void competingMeaningsAreDetectedWithoutTreatingRetrievalAsAuthorization() {
		var first = new GroundingInterpretation(List.of(), 0.91d, new EntityId(1L), "field:1", null);
		var second = new GroundingInterpretation(List.of(), 0.90d, new EntityId(1L), "field:2", null);

		var decision = new GroundingDecision("name", GroundingOutcome.REQUIRES_CLARIFICATION, null,
				List.of(first, second), "ambiguous", List.of());

		assertTrue(decision.hadCompetingMeanings());
		assertNull(decision.committed());
		assertEquals(2, decision.competingInterpretations().size());
	}
}
