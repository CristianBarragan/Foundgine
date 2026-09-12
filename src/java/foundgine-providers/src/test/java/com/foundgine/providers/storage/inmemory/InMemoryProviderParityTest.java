package com.foundgine.providers.storage.inmemory;

import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionIR;
import com.foundgine.core.execution.ExecutionIRNode;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.semantic.metadata.ColumnMetadata;
import com.foundgine.core.semantic.metadata.ColumnReference;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;
import com.foundgine.core.semantic.planning.ExecutionOperation;
import com.foundgine.core.semantic.planning.SemanticPlanAuthorizationBinding;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticOrderTerm;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.core.semantic.query.SemanticSortDirection;
import org.junit.jupiter.api.Test;

import java.math.BigDecimal;
import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.*;

class InMemoryProviderParityTest {
	private static final EntityId CUSTOMER = new EntityId(1);
	private static final EntityId ACCOUNT = new EntityId(2);
	private static final FieldId CUSTOMER_ID = new FieldId(1);
	private static final FieldId CUSTOMER_NAME = new FieldId(2);
	private static final FieldId ACCOUNT_ID = new FieldId(1);
	private static final FieldId BALANCE = new FieldId(2);
	private static final FieldId ACCOUNT_CUSTOMER_ID = new FieldId(3);
	private static final RelationshipId ACCOUNTS = new RelationshipId(1);

	@Test
	void provider_compiles_to_a_provider_specific_plan() {
		var ir = ir(scan(CUSTOMER, List.of(CUSTOMER_ID), null, null));
		var compiled = new InMemoryCompiler().compile(ir);

		assertInstanceOf(InMemoryPlan.class, compiled);
		assertEquals("in-memory", compiled.provider());
		assertSame(ir, ((InMemoryPlan) compiled).ir());
	}

	@Test
	void execution_projects_only_selected_fields_and_does_not_expose_backing_values() {
		var internalRisk = new FieldId(3);
		var metadata = customerMetadata();
		var data = new InMemoryDataSet().add(new InMemoryRow(CUSTOMER,
				Map.of(CUSTOMER_ID, 1, CUSTOMER_NAME, "Alice", internalRisk, new BigDecimal("987.65"))));

		var ir = ir(scan(CUSTOMER, List.of(CUSTOMER_NAME), null, null));
		var result = execute(metadata, data, ir, new ExecutionContext(Map.of()));
		var row = single(result);

		assertEquals(1, row.effectiveCells().size());
		assertEquals(1, row.values().size());
		assertEquals("Alice", row.values().values().iterator().next());
		assertFalse(row.values().containsValue(new BigDecimal("987.65")));
	}

	@Test
	void query_filter_order_offset_and_limit_are_applied_to_root_rows() {
		var metadata = customerMetadata();
		var data = new InMemoryDataSet().add(customer(1, "Charlie")).add(customer(2, "Alice")).add(customer(3, "Bob"))
				.add(customer(4, "Dora"));

		var options = new SemanticQueryOptions(
				new SemanticFieldFilter(CUSTOMER_ID, SemanticFilterOperator.IN, List.of(2, 3, 4)),
				List.of(new SemanticOrderTerm(CUSTOMER_NAME, SemanticSortDirection.ASC)), 2, 1, null);
		var ir = ir(scan(CUSTOMER, List.of(CUSTOMER_ID, CUSTOMER_NAME), options, null));

		var rows = execute(metadata, data, ir, new ExecutionContext(Map.of())).rows();
		assertEquals(2, rows.size());
		assertEquals(3, valueFor(rows.get(0), CUSTOMER_ID));
		assertEquals(4, valueFor(rows.get(1), CUSTOMER_ID));
	}

	@Test
	void relationship_traversal_uses_metadata_join_keys_without_leaking_the_join_key() {
		var metadata = customerAndAccountMetadata();
		var data = new InMemoryDataSet().add(new InMemoryRow(CUSTOMER, Map.of(CUSTOMER_ID, 1, CUSTOMER_NAME, "Alice")))
				.add(new InMemoryRow(ACCOUNT,
						Map.of(ACCOUNT_ID, 100, BALANCE, new BigDecimal("250.75"), ACCOUNT_CUSTOMER_ID, 1)))
				.add(new InMemoryRow(ACCOUNT,
						Map.of(ACCOUNT_ID, 200, BALANCE, new BigDecimal("10.00"), ACCOUNT_CUSTOMER_ID, 2)));

		var child = new ExecutionIRNode(1, ExecutionOperation.TRAVERSE, ACCOUNT, List.of(BALANCE), ACCOUNTS, null,
				List.of(), null, null, null);
		var root = new ExecutionIRNode(0, ExecutionOperation.SCAN, CUSTOMER, List.of(CUSTOMER_NAME), null, null,
				List.of(child), null, null, null);

		var row = single(execute(metadata, data, ir(root), new ExecutionContext(Map.of())));
		assertEquals(2, row.effectiveCells().size());
		assertTrue(row.effectiveCells().values().contains("Alice"));
		assertTrue(row.effectiveCells().values().contains(new BigDecimal("250.75")));
		assertFalse(row.effectiveCells().keySet().stream().anyMatch(x -> x.fieldId().equals(ACCOUNT_CUSTOMER_ID)));
	}

	@Test
	void authorization_filters_rows_using_resource_and_context_values() {
		var metadata = customerMetadata();
		var data = new InMemoryDataSet().add(customer(1, "Alice")).add(customer(2, "Bob"));
		var predicate = AuthorizationPredicate.equal(
				AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "Id"),
				AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("tenant"), "id"));
		var root = scan(CUSTOMER, List.of(CUSTOMER_ID, CUSTOMER_NAME), null, predicate);

		var row = single(execute(metadata, data, ir(root), new ExecutionContext(Map.of("tenant.id", 2))));
		assertEquals("Bob", valueFor(row, CUSTOMER_NAME));
	}

	@Test
	void provider_security_conformance_certifies_required_invariants() {
		var ir = new ExecutionIR(scan(CUSTOMER, List.of(CUSTOMER_ID), null, null),
				List.of("authorization.required", "visibility.field"),
				new SemanticPlanAuthorizationBinding("contract", "authorization"));
		var compiler = new InMemoryCompiler();
		var plan = compiler.compile(ir);

		var conformance = compiler.evaluate(ir, plan);
		assertTrue(conformance.isSatisfied());
		assertTrue(conformance.violations().isEmpty());
	}

	@Test
	void execution_requires_authorization_provenance() {
		var ir = ir(scan(CUSTOMER, List.of(CUSTOMER_ID), null, null));
		var metadata = customerMetadata();
		var compiler = new InMemoryCompiler(metadata, new InMemoryDataSet());

		assertThrows(IllegalStateException.class, () -> compiler.executeAsyncResult(new InMemoryPlan(ir),
				new ExecutionContext(Map.of()), CancellationToken.NONE));
	}

	private static ExecutionIRNode scan(EntityId entity, List<FieldId> fields, SemanticQueryOptions options,
			AuthorizationPredicate authorization) {
		return new ExecutionIRNode(0, ExecutionOperation.SCAN, entity, fields, null, null, List.of(), options,
				authorization, null);
	}

	private static ExecutionIR ir(ExecutionIRNode root) {
		return new ExecutionIR(root, List.of(),
				new SemanticPlanAuthorizationBinding("test-contract", "test-authorization"));
	}

	private static ExecutionResult execute(MetadataRegistry metadata, InMemoryDataSet data, ExecutionIR ir,
			ExecutionContext context) {
		var compiler = new InMemoryCompiler(metadata, data);
		return compiler.executeAsyncResult(compiler.compile(ir), context, CancellationToken.NONE);
	}

	private static InMemoryRow customer(int id, String name) {
		return new InMemoryRow(CUSTOMER, Map.of(CUSTOMER_ID, id, CUSTOMER_NAME, name));
	}

	private static Object valueFor(com.foundgine.core.execution.ExecutionRow row, FieldId field) {
		return row.effectiveCells().entrySet().stream().filter(x -> x.getKey().fieldId().equals(field)).findFirst()
				.orElseThrow().getValue();
	}

	private static com.foundgine.core.execution.ExecutionRow single(ExecutionResult result) {
		assertEquals(1, result.rows().size());
		return result.rows().get(0);
	}

	private static MetadataRegistry customerMetadata() {
		var metadata = new MetadataRegistry();
		metadata.register(new EntityMetadata(CUSTOMER, "Customer",
				List.of(new ColumnMetadata(col(11), "Id"), new ColumnMetadata(col(12), "Name")), null,
				List.of(new FieldMetadata(CUSTOMER_ID, "Id", Integer.class, ref(CUSTOMER, 11)),
						new FieldMetadata(CUSTOMER_NAME, "Name", String.class, ref(CUSTOMER, 12))),
				ref(CUSTOMER, 11), null, false, null, null));
		return metadata;
	}

	private static MetadataRegistry customerAndAccountMetadata() {
		var metadata = customerMetadata();
		metadata.register(
				new EntityMetadata(
						ACCOUNT, "Account", List.of(new ColumnMetadata(col(21), "Id"),
								new ColumnMetadata(col(22), "Balance"), new ColumnMetadata(col(23), "CustomerId")),
						null,
						List.of(new FieldMetadata(ACCOUNT_ID, "Id", Integer.class, ref(ACCOUNT, 21)),
								new FieldMetadata(BALANCE, "Balance", BigDecimal.class, ref(ACCOUNT, 22)),
								new FieldMetadata(ACCOUNT_CUSTOMER_ID, "CustomerId", Integer.class, ref(ACCOUNT, 23))),
						ref(ACCOUNT, 21), null, false, null, null));
		metadata.register(
				new RelationshipMetadata(ACCOUNTS, CUSTOMER, ACCOUNT, "Accounts", ref(CUSTOMER, 11), ref(ACCOUNT, 23)));
		return metadata;
	}

	private static ColumnId col(int value) {
		return new ColumnId(value);
	}

	private static ColumnReference ref(EntityId entity, int column) {
		return new ColumnReference(entity, col(column));
	}
}
