package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.aggregates.AggregateProviderCapability;
import com.foundgine.core.semantic.aggregates.AggregateProviderCapabilityRegistry;
import com.foundgine.core.semantic.query.SemanticAggregateFilter;
import com.foundgine.core.semantic.query.SemanticAggregateFilterOperator;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.core.semantic.query.SemanticRelationshipFilter;
import com.foundgine.core.semantic.query.SemanticRelationshipQuantifier;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.MethodSource;

import java.util.List;
import java.util.stream.Stream;

import static org.junit.jupiter.api.Assertions.*;
import static org.junit.jupiter.params.provider.Arguments.arguments;

/**
 * Port of C# {@code Foundgine.Core.Semantic.Planning.Tests.AggregateExistenceCollapseRuleTests}.
 *
 * <p>
 * <b>Porting decisions:</b>
 * <ul>
 * <li>The only existing Java coverage touching this class,
 * {@code AggregateExistenceSqlRenderingParityTest}, tests the SQL-rendering
 * effect of the rule end-to-end via {@code SqlCompiler}; nothing exercised
 * {@code AggregateExistenceCollapseRule} directly at the planning-node
 * level (rejecting providers without quantifier support, rejecting
 * comparisons that aren't provably existence/emptiness tests, or the
 * security-invariant preservation contract). This file closes that gap.</li>
 * <li>C#'s {@code Assert.Same(plan, rule.Apply(plan))} (reference equality)
 * is ported as Java reference equality via {@code assertSame}, which the
 * Java rule supports directly: {@code apply} returns the original
 * {@code SemanticPlan} instance, unchanged, when {@code canApply} is
 * false or no rewrite occurred.</li>
 * </ul>
 */
class AggregateExistenceCollapseRuleParityTest {

	private static final RelationshipId RELATIONSHIP = new RelationshipId(10);
	private static final SemanticFilterExpression PREDICATE = new SemanticFieldFilter(new FieldId(3),
			SemanticFilterOperator.EQ, "open");

	static Stream<org.junit.jupiter.params.provider.Arguments> collapsingCases() {
		return Stream.of(arguments(SemanticAggregateFilterOperator.GT, 0L, SemanticRelationshipQuantifier.SOME),
				arguments(SemanticAggregateFilterOperator.GTE, 1L, SemanticRelationshipQuantifier.SOME),
				arguments(SemanticAggregateFilterOperator.NEQ, 0L, SemanticRelationshipQuantifier.SOME),
				arguments(SemanticAggregateFilterOperator.EQ, 0L, SemanticRelationshipQuantifier.NONE),
				arguments(SemanticAggregateFilterOperator.LT, 1L, SemanticRelationshipQuantifier.NONE),
				arguments(SemanticAggregateFilterOperator.LTE, 0L, SemanticRelationshipQuantifier.NONE));
	}

	static Stream<org.junit.jupiter.params.provider.Arguments> nonCollapsingCases() {
		return Stream.of(arguments(SemanticAggregateFilterOperator.GTE, 0L),
				arguments(SemanticAggregateFilterOperator.GT, 1L), arguments(SemanticAggregateFilterOperator.LT, 0L),
				arguments(SemanticAggregateFilterOperator.LTE, -1L));
	}

	@ParameterizedTest
	@MethodSource("collapsingCases")
	void barePredicateBearingCountCollapsesToMatchingQuantifier(SemanticAggregateFilterOperator op, long value,
			SemanticRelationshipQuantifier expected) {
		var plan = planWith(new SemanticAggregateFilter(RELATIONSHIP, SemanticFilterAggregate.COUNT, null, op, value,
				PREDICATE));

		var rule = new AggregateExistenceCollapseRule(AggregateProviderCapabilityRegistry.GENERIC_SQL);
		var optimized = rule.apply(plan);

		var result = assertInstanceOf(SemanticRelationshipFilter.class, optimized.root().queryOptions().filter());
		assertEquals(RELATIONSHIP, result.relationship());
		assertEquals(expected, result.quantifier());
		assertEquals(PREDICATE, result.predicate());
		assertEquals(SemanticEquivalenceFingerprint.create(plan), SemanticEquivalenceFingerprint.create(optimized));
	}

	@ParameterizedTest
	@MethodSource("nonCollapsingCases")
	void nonExistenceCountComparisonIsNotCollapsed(SemanticAggregateFilterOperator op, long value) {
		var plan = planWith(new SemanticAggregateFilter(RELATIONSHIP, SemanticFilterAggregate.COUNT, null, op, value,
				PREDICATE));

		var optimized = new AggregateExistenceCollapseRule(AggregateProviderCapabilityRegistry.GENERIC_SQL)
				.apply(plan);

		assertSame(plan.root().queryOptions().filter(), optimized.root().queryOptions().filter());
	}

	@Test
	void providerWithoutRelationshipQuantifiersIsRejected() {
		var capability = new AggregateProviderCapability("limited", List.of(SemanticFilterAggregate.COUNT), true,
				false);
		var plan = planWith(new SemanticAggregateFilter(RELATIONSHIP, SemanticFilterAggregate.COUNT, null,
				SemanticAggregateFilterOperator.GT, 0L, PREDICATE));

		var rule = new AggregateExistenceCollapseRule(capability);

		assertFalse(rule.canApply(plan));
		assertSame(plan, rule.apply(plan));
	}

	@Test
	void existingSecurityContractIsPreserved() {
		var root = new SemanticPlanNode(1, ExecutionOperation.SCAN,
				new EntityId(1), List.of(), null, null, List.of(),
				new SemanticQueryOptions(
						new SemanticAggregateFilter(RELATIONSHIP, SemanticFilterAggregate.COUNT, null,
								SemanticAggregateFilterOperator.GT, 0L, PREDICATE),
						List.of(), null, null, null),
				null, null, null, -1, null);
		var plan = new SemanticPlan(root, List.of("tenant-isolation", "authorization.required"), null);

		var optimized = new AggregateExistenceCollapseRule(AggregateProviderCapabilityRegistry.GENERIC_SQL)
				.apply(plan);

		assertEquals(plan.effectiveSecurityInvariants().stream().sorted().toList(),
				optimized.effectiveSecurityInvariants().stream().sorted().toList());
	}

	private static SemanticPlan planWith(SemanticFilterExpression filter) {
		var root = new SemanticPlanNode(1, ExecutionOperation.SCAN,
				new EntityId(1), List.of(), null, null, List.of(),
				new SemanticQueryOptions(filter, List.of(), null, null, null),
				null, null, null, -1, null);
		return new SemanticPlan(root);
	}
}
