package com.foundgine.providers.storage.sql;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.*;
import com.foundgine.core.semantic.metadata.*;
import com.foundgine.core.semantic.planning.*;
import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.Test;

import java.sql.*;
import java.util.*;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of C# {@code Foundgine.E2E.Tests.EvidenceTests.Sql_execution_returns_provider_neutral_evidence} and
 * {@code Equivalent_sql_plans_produce_the_same_plan_fingerprint}.
 *
 * <p>
 * The C# original resolves its plan through {@code Foundgine.Generated.GeneratedMetadata.Registry} (a
 * source-generator artifact this port has no equivalent for) and executes against an in-memory SQLite database.
 * This port builds an equivalent hand-written {@link MetadataRegistry} for a tenant-scoped "contracts" entity and
 * runs against the same opt-in real-PostgreSQL pattern already used by {@code SqlExecutionPostgresE2ETest} /
 * {@code UpsertParityTest} (set {@code FOUNDGINE_POSTGRES_CONNECTION_STRING} to run these). The behavior under
 * test &mdash; that SQL execution returns provider-neutral, non-raw-text evidence, and that structurally
 * equivalent plans fingerprint identically &mdash; is preserved exactly; only the metadata source and database
 * backend differ.
 * </p>
 */
class SqlExecutionEvidenceParityTest {
	private static final EntityId CONTRACT = new EntityId(301);
	private static final ColumnId ID_COLUMN = new ColumnId(301);
	private static final ColumnId CONTRACT_TYPE_COLUMN = new ColumnId(302);
	private static final ColumnId TENANT_COLUMN = new ColumnId(303);
	private static final FieldId ID_FIELD = new FieldId(301);
	private static final FieldId CONTRACT_TYPE_FIELD = new FieldId(302);
	private static final FieldId TENANT_FIELD = new FieldId(303);

	@Test
	void sqlExecutionReturnsProviderNeutralEvidence() throws Exception {
		String url = jdbcUrl();
		Assumptions.assumeTrue(url != null, "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run this test.");
		String schema = "fg_java_evidence_" + UUID.randomUUID().toString().replace("-", "");

		try (Connection connection = DriverManager.getConnection(url)) {
			createSchema(connection, schema);
			seed(connection, schema);

			SqlPlan sqlPlan = new SqlCompiler(metadata(schema)).compile(planFor(1));

			ExecutionContext context = new ExecutionContext(Map.of("tenant.id", 7L));
			ExecutionResult result = new SqlExecutionProvider(connection)
					.executeAsync(sqlPlan, context, CancellationToken.NONE).toCompletableFuture().join();

			assertNotNull(result.evidence());
			ExecutionEvidence evidence = result.evidence();
			assertEquals("sql", evidence.provider());
			assertFalse(evidence.planFingerprint() == null || evidence.planFingerprint().isBlank());
			assertTrue(evidence.authorizedNodeIds().contains(1));
			assertEquals(1, evidence.rowsReturned());
			assertTrue(evidence.elapsedMilliseconds() >= 0);
			assertFalse(evidence.providerOperationFingerprint() == null
					|| evidence.providerOperationFingerprint().isBlank());
			assertNotEquals(sqlPlan.commandText(), evidence.providerOperationFingerprint());
		} finally {
			dropSchema(url, schema);
		}
	}

	@Test
	void equivalentSqlPlansProduceTheSamePlanFingerprint() throws Exception {
		String url = jdbcUrl();
		Assumptions.assumeTrue(url != null, "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run this test.");
		String schema = "fg_java_evidence_fp_" + UUID.randomUUID().toString().replace("-", "");

		try (Connection connection = DriverManager.getConnection(url)) {
			createSchema(connection, schema);

			SqlCompiler compiler = new SqlCompiler(metadata(schema));
			SqlPlan sql1 = compiler.compile(planFor(1));
			SqlPlan sql2 = compiler.compile(planFor(1));

			ExecutionContext context = new ExecutionContext(Map.of("tenant.id", 7L));
			ExecutionResult first = new SqlExecutionProvider(connection)
					.executeAsync(sql1, context, CancellationToken.NONE).toCompletableFuture().join();
			ExecutionResult second = new SqlExecutionProvider(connection)
					.executeAsync(sql2, context, CancellationToken.NONE).toCompletableFuture().join();

			assertNotNull(first.evidence());
			assertNotNull(second.evidence());
			assertEquals(first.evidence().planFingerprint(), second.evidence().planFingerprint());
		} finally {
			dropSchema(url, schema);
		}
	}

	private static SemanticPlan planFor(int rootId) {
		SemanticPlanNode root = new SemanticPlanNode(rootId, ExecutionOperation.SCAN, CONTRACT,
				List.of(ID_FIELD, CONTRACT_TYPE_FIELD), null, null, List.of(),
				new SemanticQueryOptions(null, List.of(), null, null, null), tenantAuthorization(), null,
				RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT);
		return new SemanticPlan(root, List.of(),
				new SemanticPlanAuthorizationBinding("test-contract", "test-authorization"));
	}

	private static AuthorizationPredicate tenantAuthorization() {
		return AuthorizationPredicate.equal(
				AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "TenantId"),
				AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("tenant"), "id"));
	}

	private static MetadataRegistry metadata(String schema) {
		MetadataRegistry registry = new MetadataRegistry();
		registry.register(new EntityMetadata(CONTRACT, "Contract",
				List.of(new ColumnMetadata(ID_COLUMN, "id"), new ColumnMetadata(CONTRACT_TYPE_COLUMN, "contract_type"),
						new ColumnMetadata(TENANT_COLUMN, "tenant_id")),
				schema + ".contracts",
				List.of(new FieldMetadata(ID_FIELD, "Id", Long.class, new ColumnReference(CONTRACT, ID_COLUMN)),
						new FieldMetadata(CONTRACT_TYPE_FIELD, "ContractType", Long.class,
								new ColumnReference(CONTRACT, CONTRACT_TYPE_COLUMN)),
						new FieldMetadata(TENANT_FIELD, "TenantId", Long.class,
								new ColumnReference(CONTRACT, TENANT_COLUMN))),
				new ColumnReference(CONTRACT, ID_COLUMN), null, false, null, null));
		return registry;
	}

	private static void createSchema(Connection c, String schema) throws SQLException {
		try (Statement s = c.createStatement()) {
			s.execute("CREATE SCHEMA \"" + schema + "\"");
			s.execute("CREATE TABLE \"" + schema
					+ "\".\"contracts\" (\"id\" BIGSERIAL PRIMARY KEY, \"contract_type\" BIGINT NOT NULL, \"tenant_id\" BIGINT NOT NULL)");
		}
	}

	private static void seed(Connection c, String schema) throws SQLException {
		try (Statement s = c.createStatement()) {
			s.execute("INSERT INTO \"" + schema
					+ "\".\"contracts\" (\"contract_type\",\"tenant_id\") VALUES (0, 7), (1, 9)");
		}
	}

	private static String jdbcUrl() {
		String value = System.getenv("FOUNDGINE_POSTGRES_CONNECTION_STRING");
		return (value == null || value.isBlank()) ? null : value;
	}

	private static void dropSchema(String url, String schema) {
		if (url == null)
			return;
		try (Connection c = DriverManager.getConnection(url); Statement s = c.createStatement()) {
			s.execute("DROP SCHEMA IF EXISTS \"" + schema + "\" CASCADE");
		} catch (SQLException ignored) {
			// Preserve the original test failure if cleanup cannot connect.
		}
	}
}
