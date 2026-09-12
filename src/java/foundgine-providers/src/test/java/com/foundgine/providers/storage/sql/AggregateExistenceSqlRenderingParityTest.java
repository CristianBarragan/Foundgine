package com.foundgine.providers.storage.sql;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.metadata.*;
import com.foundgine.core.semantic.planning.*;
import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of C# {@code Foundgine.E2E.Tests.AggregateExistenceSqlRenderingTests}.
 *
 * <p>
 * The C# original proves that the {@code AggregateExecutionStrategy} hint set
 * by {@code AggregateCardinalityOptimizationRule} is actually consumed
 * downstream: it used to be dropped silently at the plan-lowering boundary
 * and never reached the SQL compiler. {@code SqlCompilerParityTest} already
 * covers the compiler's rendering given a manually-constructed strategy, and
 * {@code AggregateExecutionStrategyResolverParityTest} already covers the
 * resolver's eligibility rules in isolation — but neither exercises the rule
 * and the compiler <em>together</em>, which is the specific regression this
 * file guards against. This test drives {@link AggregateCardinalityOptimizationRule#apply}
 * and feeds its actual output straight into {@link SqlCompiler}, using the
 * same Customer/Account fixture {@code SqlCompilerParityTest} uses (the C#
 * suite uses its own {@code Banking} sample fixture, which has no Java
 * equivalent, so the fixture here is the fixture that's actually available).
 */
class AggregateExistenceSqlRenderingParityTest {

	private static final EntityId CUSTOMER = new EntityId(301);
	private static final EntityId ACCOUNT = new EntityId(302);
	private static final RelationshipId CUSTOMER_ACCOUNTS = new RelationshipId(301);
	private static final FieldId CUSTOMER_ID = new FieldId(1);

	@Test
	void countGreaterThanZeroCompilesToExistsInsteadOfCount() {
		String sql = compileAfterRule(new SemanticAggregateFilter(CUSTOMER_ACCOUNTS, SemanticFilterAggregate.COUNT,
				null, SemanticAggregateFilterOperator.GT, 0));

		assertTrue(sql.contains("EXISTS (SELECT 1 FROM"), sql);
		assertFalse(sql.contains("COUNT(*)"), sql);
		assertFalse(sql.contains("NOT EXISTS"), sql);
	}

	@Test
	void countEqualZeroCompilesToNotExistsInsteadOfCount() {
		String sql = compileAfterRule(new SemanticAggregateFilter(CUSTOMER_ACCOUNTS, SemanticFilterAggregate.COUNT,
				null, SemanticAggregateFilterOperator.EQ, 0));

		assertTrue(sql.contains("NOT EXISTS (SELECT 1 FROM"), sql);
		assertFalse(sql.contains("COUNT(*)"), sql);
	}

	@ParameterizedTest
	@CsvSource({ "GTE, 0", "LT, 0" })
	void constantCountComparisonsAreNotMiscompiledAsExistenceTests(SemanticAggregateFilterOperator op, long value) {
		// COUNT >= 0 is always true and COUNT < 0 is always false for a
		// non-negative COUNT. Neither is equivalent to EXISTS/NOT EXISTS, so
		// the optimizer must leave the aggregate strategy at DEFAULT rather
		// than applying an existence rewrite.
		var plan = applyRule(new SemanticAggregateFilter(CUSTOMER_ACCOUNTS, SemanticFilterAggregate.COUNT, null, op,
				value));

		assertEquals(AggregateExecutionStrategy.DEFAULT, plan.root().aggregateExecutionStrategy());

		String sql = compile(plan);
		assertTrue(sql.contains("COUNT(*)"), sql);
		assertFalse(sql.contains("EXISTS"), sql);
	}

	@Test
	void countGreaterThanOneIsNotEligibleStillCompilesToCount() {
		// The rule never assigns a strategy to this comparison (it genuinely
		// depends on the exact count), so the strategy stays DEFAULT and the
		// SQL writer must fall back to the original COUNT-subquery rendering.
		var plan = applyRule(new SemanticAggregateFilter(CUSTOMER_ACCOUNTS, SemanticFilterAggregate.COUNT, null,
				SemanticAggregateFilterOperator.GT, 1));

		assertEquals(AggregateExecutionStrategy.DEFAULT, plan.root().aggregateExecutionStrategy());

		String sql = compile(plan);
		assertTrue(sql.contains("COUNT(*)"), sql);
		assertFalse(sql.contains("EXISTS"), sql);
	}

	@Test
	void withoutRunningTheRuleDefaultStrategyStillCompilesToCount() {
		// Compiling the un-optimized plan directly (strategy left at its
		// default) must preserve the pre-existing COUNT-subquery output
		// exactly, so provider output for callers that never run the
		// optimizer does not silently change.
		var plan = createPlan(new SemanticAggregateFilter(CUSTOMER_ACCOUNTS, SemanticFilterAggregate.COUNT, null,
				SemanticAggregateFilterOperator.GT, 0));

		String sql = compile(plan);
		assertTrue(sql.contains("COUNT(*)"), sql);
		assertFalse(sql.contains("EXISTS"), sql);
	}

	private static String compileAfterRule(SemanticAggregateFilter filter) {
		return compile(applyRule(filter));
	}

	private static SemanticPlan applyRule(SemanticAggregateFilter filter) {
		return new AggregateCardinalityOptimizationRule().apply(createPlan(filter));
	}

	private static String compile(SemanticPlan plan) {
		return new SqlCompiler(metadata()).compile(plan).commandText();
	}

	private static SemanticPlan createPlan(SemanticAggregateFilter filter) {
		var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, CUSTOMER, List.of(CUSTOMER_ID), null, null,
				List.of(), new SemanticQueryOptions(filter, List.of(), null, null, null), null, null, null, -1,
				AggregateExecutionStrategy.DEFAULT);
		return new SemanticPlan(node, List.of(), new SemanticPlanAuthorizationBinding("test-contract",
				"test-authorization"));
	}

	private static MetadataRegistry metadata() {
		var registry = new MetadataRegistry();
		var customerId = new ColumnId(1);
		var accountId = new ColumnId(2);
		var accountCustomerId = new ColumnId(3);

		registry.register(new EntityMetadata(CUSTOMER, "Customer", List.of(new ColumnMetadata(customerId, "Id")),
				"Customer",
				List.of(new FieldMetadata(CUSTOMER_ID, "Id", Long.class, new ColumnReference(CUSTOMER, customerId))),
				new ColumnReference(CUSTOMER, customerId), null, false, null, null));

		registry.register(new EntityMetadata(ACCOUNT, "Account",
				List.of(new ColumnMetadata(accountId, "Id"), new ColumnMetadata(accountCustomerId, "CustomerId")),
				"Account",
				List.of(new FieldMetadata(new FieldId(2), "Id", Long.class, new ColumnReference(ACCOUNT, accountId)),
						new FieldMetadata(new FieldId(3), "CustomerId", Long.class,
								new ColumnReference(ACCOUNT, accountCustomerId))),
				new ColumnReference(ACCOUNT, accountId), null, false, null, null));

		registry.register(new RelationshipMetadata(CUSTOMER_ACCOUNTS, CUSTOMER, ACCOUNT, "Accounts",
				new ColumnReference(CUSTOMER, customerId), new ColumnReference(ACCOUNT, accountCustomerId), true,
				null));
		return registry;
	}
}
