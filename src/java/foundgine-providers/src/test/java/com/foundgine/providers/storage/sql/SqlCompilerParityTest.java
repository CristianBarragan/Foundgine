package com.foundgine.providers.storage.sql;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.metadata.*;
import com.foundgine.core.semantic.planning.*;
import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Test;

import java.util.*;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Provider-level SQL rendering contracts mirrored from the C# E2E SQL pipeline
 * suite. These tests intentionally inspect the provider-neutral plan ->
 * physical SQL boundary without requiring a live database.
 */
class SqlCompilerParityTest {
	private static final EntityId CUSTOMER = new EntityId(301);
	private static final EntityId ACCOUNT = new EntityId(302);
	private static final RelationshipId CUSTOMER_ACCOUNTS = new RelationshipId(301);

	private static final FieldId CUSTOMER_ID = new FieldId(1);
	private static final FieldId CUSTOMER_NAME = new FieldId(2);
	private static final FieldId ACCOUNT_ID = new FieldId(3);
	private static final FieldId ACCOUNT_CUSTOMER_ID = new FieldId(4);
	private static final FieldId ACCOUNT_BALANCE = new FieldId(5);

	@Test
	void aggregateCountGreaterThanZeroUsesCountByDefault() {
		var filter = new SemanticAggregateFilter(CUSTOMER_ACCOUNTS, SemanticFilterAggregate.COUNT, null,
				SemanticAggregateFilterOperator.GT, 0);

		var sql = compile(new SemanticQueryOptions(filter, List.of(), null, null, null)).commandText();

		assertTrue(sql.contains("COUNT(*)"), sql);
		assertFalse(sql.contains("EXISTS (SELECT 1 FROM"), sql);
	}

	@Test
	void optimizedAggregateCountGreaterThanZeroUsesExists() {
		var filter = new SemanticAggregateFilter(CUSTOMER_ACCOUNTS, SemanticFilterAggregate.COUNT, null,
				SemanticAggregateFilterOperator.GT, 0);

		var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, CUSTOMER, List.of(CUSTOMER_ID), null, null,
				List.of(), new SemanticQueryOptions(filter, null, null, null, null), null, null, null, -1,
				AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT);

		var sql = new SqlCompiler(metadata()).compile(new SemanticPlan(node, List.of(),
				new SemanticPlanAuthorizationBinding("test-contract", "test-authorization"))).commandText();

		assertTrue(sql.contains("EXISTS (SELECT 1 FROM"), sql);
		assertFalse(sql.contains("COUNT(*)"), sql);
	}

	@Test
	void aggregateCountGreaterThanOneRemainsCount() {
		var filter = new SemanticAggregateFilter(CUSTOMER_ACCOUNTS, SemanticFilterAggregate.COUNT, null,
				SemanticAggregateFilterOperator.GT, 1);

		var sql = compile(new SemanticQueryOptions(filter, List.of(), null, null, null)).commandText();

		assertTrue(sql.contains("COUNT(*)"), sql);
		assertFalse(sql.contains("EXISTS (SELECT 1 FROM"), sql);
	}

	@Test
	void relationshipFilterUsesExistsAndDoesNotIntroduceTopLevelJoin() {
		var filter = new SemanticRelationshipFilter(CUSTOMER_ACCOUNTS, SemanticRelationshipQuantifier.SOME,
				new SemanticFieldFilter(ACCOUNT_BALANCE, SemanticFilterOperator.EQ, 100));

		var sql = compile(new SemanticQueryOptions(filter, List.of(), null, null, null)).commandText();

		assertTrue(sql.contains("EXISTS (SELECT 1 FROM \"Account\""), sql);
		assertFalse(sql.contains("INNER JOIN"), sql);
		assertTrue(sql.contains("@p0"), sql);
	}

	@Test
	void relationshipFilterAllUsesNotExistsWithNegatedPredicate() {
		var filter = new SemanticRelationshipFilter(CUSTOMER_ACCOUNTS, SemanticRelationshipQuantifier.ALL,
				new SemanticFieldFilter(ACCOUNT_BALANCE, SemanticFilterOperator.NEQ, 0));

		var sql = compile(new SemanticQueryOptions(filter, List.of(), null, null, null)).commandText();

		assertTrue(sql.contains("NOT EXISTS (SELECT 1 FROM \"Account\""), sql);
		assertTrue(sql.contains("AND NOT ("), sql);
	}

	@Test
	void storageNameWithSchemaIsQuotedAsSeparateIdentifiers() {
		var metadata = metadata("Banking.Customer", "Banking.Account");
		var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, CUSTOMER, List.of(CUSTOMER_ID, CUSTOMER_NAME), null,
				null, List.of(), new SemanticQueryOptions(), null, null, null, 0, null);

		var sql = new SqlCompiler(metadata).compile(new SemanticPlan(node, List.of(),
				new SemanticPlanAuthorizationBinding("test-contract", "test-authorization"))).commandText();

		assertTrue(sql.contains("FROM \"Banking\".\"Customer\""), sql);
		assertFalse(sql.contains("FROM \"Banking.Customer\""), sql);
	}

	@Test
	void limitAndOffsetBecomeExecutionContextParameters() {
		var options = new SemanticQueryOptions(null, List.of(), 25, 10, null);
		var plan = new SemanticPlan(
				new SemanticPlanNode(1, ExecutionOperation.SCAN, CUSTOMER, List.of(CUSTOMER_ID), null, null, List.of(),
						options, null, null, null, 0, null),
				List.of(), new SemanticPlanAuthorizationBinding("test-contract", "test-authorization"));

		var compiled = new SqlCompiler(metadata()).compile(plan);

		assertTrue(compiled.commandText().contains("LIMIT @__fg_limit"), compiled.commandText());
		assertTrue(compiled.commandText().contains("OFFSET @__fg_offset"), compiled.commandText());
		assertTrue(compiled.parameters().stream().anyMatch(p -> p.name().equals("__fg_limit")));
		assertTrue(compiled.parameters().stream().anyMatch(p -> p.name().equals("__fg_offset")));
	}

	private static SqlPlan compile(SemanticQueryOptions options) {
		var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, CUSTOMER, List.of(CUSTOMER_ID), null, null,
				List.of(), options, null, null, null, 0, null);
		return new SqlCompiler(metadata()).compile(new SemanticPlan(node, List.of(),
				new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")));
	}

	private static MetadataRegistry metadata() {
		return metadata("Customer", "Account");
	}

	private static MetadataRegistry metadata(String customerStorage, String accountStorage) {
		var registry = new MetadataRegistry();
		var customerId = new ColumnId(1);
		var customerName = new ColumnId(2);
		var accountId = new ColumnId(3);
		var accountCustomerId = new ColumnId(4);
		var accountBalance = new ColumnId(5);

		registry.register(new EntityMetadata(CUSTOMER, "Customer",
				List.of(new ColumnMetadata(customerId, "Id"), new ColumnMetadata(customerName, "Name")),
				customerStorage,
				List.of(new FieldMetadata(CUSTOMER_ID, "Id", Long.class, new ColumnReference(CUSTOMER, customerId)),
						new FieldMetadata(CUSTOMER_NAME, "Name", String.class,
								new ColumnReference(CUSTOMER, customerName))),
				new ColumnReference(CUSTOMER, customerId), null, false, null, null));

		registry.register(new EntityMetadata(ACCOUNT, "Account", List.of(new ColumnMetadata(accountId, "Id"),
				new ColumnMetadata(accountCustomerId, "CustomerId"), new ColumnMetadata(accountBalance, "Balance")),
				accountStorage,
				List.of(new FieldMetadata(ACCOUNT_ID, "Id", Long.class, new ColumnReference(ACCOUNT, accountId)),
						new FieldMetadata(ACCOUNT_CUSTOMER_ID, "CustomerId", Long.class,
								new ColumnReference(ACCOUNT, accountCustomerId)),
						new FieldMetadata(ACCOUNT_BALANCE, "Balance", Long.class,
								new ColumnReference(ACCOUNT, accountBalance))),
				new ColumnReference(ACCOUNT, accountId), null, false, null, null));

		registry.register(new RelationshipMetadata(CUSTOMER_ACCOUNTS, CUSTOMER, ACCOUNT, "Accounts",
				new ColumnReference(CUSTOMER, customerId), new ColumnReference(ACCOUNT, accountCustomerId), true,
				null));
		return registry;
	}
}
