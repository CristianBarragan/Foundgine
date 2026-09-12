package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
import java.util.*;

class RewriteRuleComposerParityTest {
	private static SemanticPlan plan() {
		return new SemanticPlan(new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
				List.of(new FieldId(1), new FieldId(1)), null, null, List.of()));
	}

	@Test
	void composerAppliesDeterministicRewriteAndStops() {
		var composer = new RewriteRuleComposer(List.of(new ProjectionPruningRule()));
		var result = composer.compose(plan());
		assertTrue(result.terminatedNormally());
		assertEquals(List.of(new FieldId(1)), result.plan().root().fields());
		assertEquals(1, result.applications().size());
	}

	@Test
	void composerRejectsInvalidApplicationBudget() {
		assertThrows(IllegalArgumentException.class, () -> new RewriteRuleComposer(List.of(new ProjectionPruningRule()),
				new RewriteRuleCompositionOptions(0, 1)));
	}

	@Test
	void composerHonorsPlanVisitBudget() {
		assertThrows(IllegalArgumentException.class, () -> new RewriteRuleComposer(List.of(new ProjectionPruningRule()),
				new RewriteRuleCompositionOptions(1, 0)));
	}
}
