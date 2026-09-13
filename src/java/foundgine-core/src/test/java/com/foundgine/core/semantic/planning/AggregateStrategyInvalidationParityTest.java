package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.query.SemanticAggregateFilter;
import com.foundgine.core.semantic.query.SemanticAndFilter;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticAggregateFilterOperator;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.core.semantic.query.SemanticRelationshipFilter;
import com.foundgine.core.semantic.query.SemanticRelationshipQuantifier;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotNull;

/**
 * Port of {@code Foundgine.E2E.Tests.AggregateStrategyInvalidationTests}.
 *
 * <p>
 * Both cases start a plan node with a stale, previously-computed
 * {@code AggregateExecutionStrategy} hint and check that the corresponding
 * rewrite rule clears it back to {@code DEFAULT} once the filter shape it was
 * computed from is no longer eligible for that optimization. This is a
 * distinct behavior from what the existing rewrite-rule parity tests cover:
 * <ul>
 * <li>{@code AggregateRewriteParityTest} only starts nodes at
 * {@code AggregateExecutionStrategy.DEFAULT}, so it never exercises the "clear
 * a stale non-default hint" path of
 * {@code AggregateCardinalityOptimizationRule}.</li>
 * <li>{@code AggregateRelationshipFilterPushdownParityTest}'s
 * {@code someRelationshipPredicateIsPushedIntoCount} rewrites the same filter
 * shape as this test's second case, but its input node's strategy is already
 * {@code DEFAULT}/{@code null}, and it never asserts the resulting node's
 * {@code aggregateExecutionStrategy()} at all — only the rewritten filter
 * shape.</li>
 * </ul>
 */
class AggregateStrategyInvalidationParityTest {

	private static SemanticPlan plan(SemanticFilterExpression filter, AggregateExecutionStrategy strategy) {
		var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1), List.of(), null, null,
				List.of(), new SemanticQueryOptions(filter, List.of(), null, null, null), null, null,
				RelationshipTraversalMode.DEFAULT, -1, strategy);
		return new SemanticPlan(node);
	}

	@Test
	void cardinalityRuleClearsStaleHintWhenFilterIsNoLongerEligible() {
		var relationship = new RelationshipId(1);
		var filter = new SemanticAggregateFilter(relationship, SemanticFilterAggregate.COUNT, null,
				SemanticAggregateFilterOperator.GT, 0,
				new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.EQ, true));

		var before = plan(filter, AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT);
		var after = new AggregateCardinalityOptimizationRule().apply(before);

		assertEquals(AggregateExecutionStrategy.DEFAULT, after.root().aggregateExecutionStrategy());
	}

	@Test
	void pushdownRuleClearsPreexistingCardinalityHint() {
		var relationship = new RelationshipId(1);
		var predicate = new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.EQ, true);
		var filter = new SemanticAndFilter(List.of(
				new SemanticAggregateFilter(relationship, SemanticFilterAggregate.COUNT, null,
						SemanticAggregateFilterOperator.GT, 0),
				new SemanticRelationshipFilter(relationship, SemanticRelationshipQuantifier.SOME, predicate)));

		var before = plan(filter, AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT);
		var after = new AggregateRelationshipFilterPushdownRule().apply(before);

		assertEquals(AggregateExecutionStrategy.DEFAULT, after.root().aggregateExecutionStrategy());
		var aggregate = assertInstanceOf(SemanticAggregateFilter.class, after.root().queryOptions().filter());
		assertNotNull(aggregate.predicate());
	}
}
