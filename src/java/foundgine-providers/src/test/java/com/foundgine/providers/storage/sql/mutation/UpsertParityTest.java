package com.foundgine.providers.storage.sql.mutation;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.MutationEntitySchema;
import com.foundgine.core.abstractions.MutationRelationshipSchema;
import com.foundgine.core.abstractions.MutationSchema;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.semantic.metadata.ColumnMetadata;
import com.foundgine.core.semantic.metadata.ColumnReference;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.planning.mutation.MutationFieldValue;
import com.foundgine.core.semantic.planning.mutation.MutationPlanner;
import com.foundgine.core.semantic.planning.mutation.UpsertIntent;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.Test;

import java.sql.Connection;
import java.sql.DriverManager;
import java.util.List;
import java.util.NoSuchElementException;
import java.util.Set;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of C# {@code Foundgine.E2E.Tests.UpsertTests}.
 *
 * <p>
 * The C# original executes against an in-memory SQLite database
 * ({@code Microsoft.Data.Sqlite}). The Java port has no embedded-database test
 * dependency, so the compile-only assertions (which don't need a live
 * connection) are ported directly, and the execution-based assertions are
 * ported against the same opt-in real PostgreSQL pattern already used by
 * {@code SqlExecutionPostgresE2ETest} / the benchmark-agent E2E suite (set
 * {@code FOUNDGINE_POSTGRES_CONNECTION_STRING} to run them).
 */
class UpsertParityTest {
	private static final EntityId CUSTOMER = new EntityId(501);
	private static final FieldId CUSTOMER_ID = new FieldId(1);
	private static final FieldId CUSTOMER_NAME = new FieldId(2);
	private static final ColumnId ID_COLUMN = new ColumnId(1);
	private static final ColumnId NAME_COLUMN = new ColumnId(2);

	@Test
	void customConflictIdentityIsUsed() {
		var metadata = buildMetadata();
		var intent = new UpsertIntent(CUSTOMER, List.of(new MutationFieldValue(NAME_COLUMN, "Alice")),
				List.of(NAME_COLUMN), List.of(CUSTOMER_ID, CUSTOMER_NAME));

		var sql = new SqlMutationCompiler(metadata).compile(new MutationPlanner(schema()).plan(intent));

		assertTrue(sql.commandText().contains("ON CONFLICT (\"Name\")"), sql.commandText());
	}

	@Test
	void unchangedUpsertUsesDistinctGuard() {
		var metadata = buildMetadata();
		var intent = new UpsertIntent(CUSTOMER,
				List.of(new MutationFieldValue(ID_COLUMN, 1), new MutationFieldValue(NAME_COLUMN, "Alice")), null,
				List.of(CUSTOMER_ID, CUSTOMER_NAME));

		var plan = new SqlMutationCompiler(metadata).compile(new MutationPlanner(schema()).plan(intent));

		assertTrue(plan.commandText().contains("IS DISTINCT FROM EXCLUDED."), plan.commandText());
		assertNotNull(plan.fallbackCommandText());
		assertTrue(plan.fallbackCommandText().contains("IS NOT DISTINCT FROM"), plan.fallbackCommandText());
	}

	@Test
	void insertWithoutPrimaryKeyReturnsGeneratedIdentity() throws Exception {
		String url = jdbcUrl();
		Assumptions.assumeTrue(url != null, "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run this test.");
		String table = "fg_java_upsert_" + UUID.randomUUID().toString().replace("-", "");

		try (Connection connection = DriverManager.getConnection(url)) {
			createTable(connection, table);
			var metadata = buildMetadata(table);

			var intent = new UpsertIntent(CUSTOMER, List.of(new MutationFieldValue(NAME_COLUMN, "Alice")), null,
					List.of(CUSTOMER_ID, CUSTOMER_NAME));
			var plan = new SqlMutationCompiler(metadata).compile(new MutationPlanner(schema()).plan(intent));
			var result = new SqlMutationExecutionProvider(connection).execute(plan, ExecutionContext.EMPTY);

			assertEquals(1, result.affectedRows());
			assertEquals(1L, result.returnedValues().get(CUSTOMER_ID));
			assertEquals("Alice", result.returnedValues().get(CUSTOMER_NAME));
		} finally {
			dropTable(url, table);
		}
	}

	@Test
	void existingPrimaryKeyIsUpdatedAndReturned() throws Exception {
		String url = jdbcUrl();
		Assumptions.assumeTrue(url != null, "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run this test.");
		String table = "fg_java_upsert_" + UUID.randomUUID().toString().replace("-", "");

		try (Connection connection = DriverManager.getConnection(url)) {
			createTable(connection, table);
			seedRow(connection, table, "Alice");
			var metadata = buildMetadata(table);

			var intent = new UpsertIntent(CUSTOMER,
					List.of(new MutationFieldValue(ID_COLUMN, 1), new MutationFieldValue(NAME_COLUMN, "Bob")), null,
					List.of(CUSTOMER_ID, CUSTOMER_NAME));
			var plan = new SqlMutationCompiler(metadata).compile(new MutationPlanner(schema()).plan(intent));
			var result = new SqlMutationExecutionProvider(connection).execute(plan, ExecutionContext.EMPTY);

			assertEquals(1, result.affectedRows());
			assertEquals(1L, result.returnedValues().get(CUSTOMER_ID));
			assertEquals("Bob", result.returnedValues().get(CUSTOMER_NAME));
		} finally {
			dropTable(url, table);
		}
	}

	@Test
	void unchangedUpsertStillReturnsExistingRow() throws Exception {
		String url = jdbcUrl();
		Assumptions.assumeTrue(url != null, "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run this test.");
		String table = "fg_java_upsert_" + UUID.randomUUID().toString().replace("-", "");

		try (Connection connection = DriverManager.getConnection(url)) {
			createTable(connection, table);
			seedRow(connection, table, "Alice");
			var metadata = buildMetadata(table);

			var intent = new UpsertIntent(CUSTOMER,
					List.of(new MutationFieldValue(ID_COLUMN, 1), new MutationFieldValue(NAME_COLUMN, "Alice")), null,
					List.of(CUSTOMER_ID, CUSTOMER_NAME));
			var plan = new SqlMutationCompiler(metadata).compile(new MutationPlanner(schema()).plan(intent));
			var result = new SqlMutationExecutionProvider(connection).execute(plan, ExecutionContext.EMPTY);

			assertEquals(1, result.affectedRows());
			assertEquals(1L, result.returnedValues().get(CUSTOMER_ID));
			assertEquals("Alice", result.returnedValues().get(CUSTOMER_NAME));
		} finally {
			dropTable(url, table);
		}
	}

	private static String jdbcUrl() {
		var value = System.getenv("FOUNDGINE_POSTGRES_CONNECTION_STRING");
		return (value == null || value.isBlank()) ? null : value;
	}

	private static void createTable(Connection connection, String table) throws java.sql.SQLException {
		try (var st = connection.createStatement()) {
			st.executeUpdate("CREATE TABLE \"" + table + "\" (\"Id\" BIGSERIAL PRIMARY KEY, \"Name\" TEXT NOT NULL)");
		}
	}

	private static void seedRow(Connection connection, String table, String name) throws java.sql.SQLException {
		try (var st = connection.createStatement()) {
			st.executeUpdate("INSERT INTO \"" + table + "\" (\"Name\") VALUES ('" + name + "')");
		}
	}

	private static void dropTable(String url, String table) {
		if (url == null)
			return;
		try (Connection connection = DriverManager.getConnection(url); var st = connection.createStatement()) {
			st.executeUpdate("DROP TABLE IF EXISTS \"" + table + "\"");
		} catch (java.sql.SQLException ignored) {
			// best-effort cleanup
		}
	}

	private static MutationSchema schema() {
		var entity = new MutationEntitySchema(CUSTOMER, "Customer", Set.of(ID_COLUMN, NAME_COLUMN),
				java.util.Map.of(CUSTOMER_ID, ID_COLUMN, CUSTOMER_NAME, NAME_COLUMN), ID_COLUMN);
		return new MutationSchema() {
			public MutationEntitySchema getEntity(EntityId id) {
				return entity;
			}

			public MutationRelationshipSchema getRelationship(RelationshipId id) {
				throw new NoSuchElementException(id.toString());
			}
		};
	}

	private static MetadataRegistry buildMetadata() {
		return buildMetadata("Customer");
	}

	private static MetadataRegistry buildMetadata(String storageName) {
		var registry = new MetadataRegistry();
		registry.register(new EntityMetadata(CUSTOMER, "Customer",
				List.of(new ColumnMetadata(ID_COLUMN, "Id"), new ColumnMetadata(NAME_COLUMN, "Name")), storageName,
				List.of(new FieldMetadata(CUSTOMER_ID, "Id", Long.class, new ColumnReference(CUSTOMER, ID_COLUMN)),
						new FieldMetadata(CUSTOMER_NAME, "Name", String.class,
								new ColumnReference(CUSTOMER, NAME_COLUMN))),
				new ColumnReference(CUSTOMER, ID_COLUMN), null, false, null, null));
		return registry;
	}
}
