package com.foundgine.providers.storage.sql;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.*;
import com.foundgine.core.semantic.metadata.*;
import com.foundgine.core.semantic.planning.*;
import com.foundgine.core.semantic.query.*;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import com.foundgine.providers.storage.sql.query.CursorCodec;
import org.junit.jupiter.api.Test;

import java.util.*;

import static org.junit.jupiter.api.Assertions.*;

/**
 * SQL provider parity tests for the two stateful boundaries that are easy to
 * get subtly wrong: compound keyset cursors and authorization predicates. These
 * mirror the C# provider's physical SQL contracts without requiring a live
 * PostgreSQL server.
 */
class SqlCursorAuthorizationParityTest {
	private static final EntityId CUSTOMER = new EntityId(301);
	private static final FieldId ID = new FieldId(1);
	private static final FieldId NAME = new FieldId(2);

	@Test
	void cursorOrderingAddsPrimaryKeyTieBreaker() {
		var options = new SemanticQueryOptions(null, List.of(new SemanticOrderTerm(NAME, SemanticSortDirection.ASC)),
				10, null, CursorCodec.encode(List.of("Bob", 7L)));

		var node = new ExecutionIRNode(1, ExecutionOperation.SCAN, CUSTOMER, List.of(ID, NAME), null, null, List.of(),
				options, null, AggregateExecutionStrategy.DEFAULT);

		var plan = new SqlCompiler(metadata()).compile(
				new ExecutionIR(node, List.of(), new SemanticPlanAuthorizationBinding("contract", "authorization")));

		assertTrue(plan.commandText().contains("ORDER BY \"t1\".\"Name\" ASC, \"t1\".\"Id\" ASC"), plan.commandText());
		assertTrue(plan.commandText().contains("AND \"t1\".\"Id\" > @"), plan.commandText());
		assertEquals(1, plan.parameters().stream().filter(p -> p.name().equals("__fg_limit")).count());
	}

	@Test
	void descendingCursorUsesLessThanForSeek() {
		var options = new SemanticQueryOptions(null, List.of(new SemanticOrderTerm(NAME, SemanticSortDirection.DESC)),
				10, null, CursorCodec.encode(List.of("Bob", 7L)));

		var node = new ExecutionIRNode(1, ExecutionOperation.SCAN, CUSTOMER, List.of(ID, NAME), null, null, List.of(),
				options, null, AggregateExecutionStrategy.DEFAULT);

		var plan = new SqlCompiler(metadata()).compile(
				new ExecutionIR(node, List.of(), new SemanticPlanAuthorizationBinding("contract", "authorization")));

		assertTrue(plan.commandText().contains("\"t1\".\"Name\" < @"), plan.commandText());
		assertTrue(plan.commandText().contains("\"t1\".\"Name\" = @"), plan.commandText());
		assertTrue(plan.commandText().contains("\"t1\".\"Id\" > @"), plan.commandText());
		assertTrue(plan.commandText().contains("ORDER BY \"t1\".\"Name\" DESC, \"t1\".\"Id\" ASC"), plan.commandText());
	}

	@Test
	void cursorWithWrongArityFailsClosed() {
		var options = new SemanticQueryOptions(null, List.of(new SemanticOrderTerm(NAME, SemanticSortDirection.ASC)),
				10, null, CursorCodec.encode(List.of("Bob")));

		var node = new ExecutionIRNode(1, ExecutionOperation.SCAN, CUSTOMER, List.of(ID, NAME), null, null, List.of(),
				options, null, AggregateExecutionStrategy.DEFAULT);

		assertThrows(IllegalArgumentException.class, () -> new SqlCompiler(metadata()).compile(
				new ExecutionIR(node, List.of(), new SemanticPlanAuthorizationBinding("contract", "authorization"))));
	}

	@Test
	void authorizationResourceAndContextMembersRemainParameterized() {
		var resource = AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "Id");
		var tenant = AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("tenant"), "Id");
		var predicate = AuthorizationPredicate.equal(resource, tenant);

		var node = new ExecutionIRNode(1, ExecutionOperation.SCAN, CUSTOMER, List.of(ID), null, null, List.of(), null,
				predicate, AggregateExecutionStrategy.DEFAULT);

		var plan = new SqlCompiler(metadata())
				.compile(new ExecutionIR(node, List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED),
						new SemanticPlanAuthorizationBinding("contract", "authorization")));

		assertTrue(plan.commandText().contains("\"t1\".\"Id\" = @auth0"), plan.commandText());
		assertEquals("tenant.Id", plan.parameters().stream().filter(p -> p.name().equals("auth0")).findFirst()
				.orElseThrow().contextPath());
		assertNull(plan.parameters().stream().filter(p -> p.name().equals("auth0")).findFirst().orElseThrow().value());
	}

	@Test
	void authorizationBooleanCompositionPreservesParentheses() {
		var left = AuthorizationPredicate.equal(
				AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "Id"),
				AuthorizationPredicate.constant("7"));
		var right = AuthorizationPredicate.notEqual(
				AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "Id"),
				AuthorizationPredicate.constant("8"));
		var predicate = AuthorizationPredicate.and(left, AuthorizationPredicate.not(right));

		var node = new ExecutionIRNode(1, ExecutionOperation.SCAN, CUSTOMER, List.of(ID), null, null, List.of(), null,
				predicate, AggregateExecutionStrategy.DEFAULT);

		var plan = new SqlCompiler(metadata())
				.compile(new ExecutionIR(node, List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED),
						new SemanticPlanAuthorizationBinding("contract", "authorization")));

		assertTrue(plan.commandText().contains("((\"t1\".\"Id\" = @auth0) AND NOT ((\"t1\".\"Id\" <> @auth1)))"),
				plan.commandText());
	}

	@Test
	void cursorCodecRoundTripsTemporalAndDecimalValues() {
		var cursor = CursorCodec.encode(List.of("2026-09-12T10:15:30Z", "12.50", 42L));
		var values = CursorCodec.decode(cursor);

		assertEquals("2026-09-12T10:15:30Z",
				CursorCodec.convertValue(values.get(0), java.time.Instant.class).toString());
		assertEquals("12.50", CursorCodec.convertValue(values.get(1), java.math.BigDecimal.class).toString());
		assertEquals(42L, CursorCodec.convertValue(values.get(2), Long.class));
	}

	private static MetadataRegistry metadata() {
		var registry = new MetadataRegistry();
		var idColumn = new ColumnId(1);
		var nameColumn = new ColumnId(2);
		registry.register(new EntityMetadata(CUSTOMER, "Customer",
				List.of(new ColumnMetadata(idColumn, "Id"), new ColumnMetadata(nameColumn, "Name")), "Customer",
				List.of(new FieldMetadata(ID, "Id", Long.class, new ColumnReference(CUSTOMER, idColumn)),
						new FieldMetadata(NAME, "Name", String.class, new ColumnReference(CUSTOMER, nameColumn))),
				new ColumnReference(CUSTOMER, idColumn), null, false, null, null));
		return registry;
	}
}
