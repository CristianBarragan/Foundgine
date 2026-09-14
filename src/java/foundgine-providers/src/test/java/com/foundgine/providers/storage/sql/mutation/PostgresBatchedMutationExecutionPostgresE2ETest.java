package com.foundgine.providers.storage.sql.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.CancellationTokenSource;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.mutation.ExecutionMutationIR;
import com.foundgine.core.execution.mutation.MutationBatchResult;
import com.foundgine.core.semantic.metadata.*;
import com.foundgine.core.semantic.planning.mutation.*;
import com.foundgine.providers.storage.sql.mutation.postgres.PostgresBatchedMutationExecutionProvider;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.Assumptions;

import java.sql.*;
import java.util.*;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Real PostgreSQL execution parity tests. These are intentionally opt-in so a
 * normal Java build does not require PostgreSQL to be running.
 *
 * <p>
 * Set {@code FOUNDGINE_POSTGRES_CONNECTION_STRING} to the JDBC URL, for example
 * {@code jdbc:postgresql://localhost:55432/foundgine_e2e?user=foundgine&password=foundgine}.
 * An ADO.NET-style Foundgine connection string is also accepted and converted
 * by {@link #jdbcUrl()} for parity with the repository's C# E2E scripts.
 */
class PostgresBatchedMutationExecutionPostgresE2ETest {
	private static final EntityId CUSTOMER = new EntityId(11);
	private static final EntityId ACCOUNT = new EntityId(12);
	private static final ColumnId CUSTOMER_ID = new ColumnId(1);
	private static final ColumnId CUSTOMER_NAME = new ColumnId(2);
	private static final ColumnId ACCOUNT_ID = new ColumnId(3);
	private static final ColumnId ACCOUNT_CUSTOMER_ID = new ColumnId(4);
	private static final ColumnId ACCOUNT_NAME = new ColumnId(5);
	private static final FieldId CUSTOMER_ID_FIELD = new FieldId(1);
	private static final FieldId CUSTOMER_NAME_FIELD = new FieldId(2);
	private static final FieldId ACCOUNT_ID_FIELD = new FieldId(3);
	private static final FieldId ACCOUNT_CUSTOMER_ID_FIELD = new FieldId(4);
	private static final FieldId ACCOUNT_NAME_FIELD = new FieldId(5);

	@Test
	void realPostgres_executesGeneratedIdentityAcrossDependencyLevels_andRollsBackCallerTransaction() throws Exception {
		String url = jdbcUrl();
		Assumptions.assumeTrue(url != null, "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run PostgreSQL E2E tests.");

		try (Connection connection = DriverManager.getConnection(url)) {
			ensureCorrelationSchema(connection);
			// The test controls the transaction so the provider must participate
			// without committing or rolling it back.
			connection.setAutoCommit(false);
			long beforeCustomers = count(connection, "fg_correlation", "Customer");
			long beforeAccounts = count(connection, "fg_correlation", "Account");

			ExecutionMutationIR ir = ExecutionMutationIR.from(dependencyBatch());
			PostgresBatchedMutationExecutionProvider provider = new PostgresBatchedMutationExecutionProvider(connection,
					metadata(), false);

			MutationBatchResult result = provider.executeBatch(ir, ExecutionContext.EMPTY);

			assertEquals(3, result.results().size());
			assertEquals(1, result.results().get(0).affectedRows());
			assertEquals(1, result.results().get(1).affectedRows());
			assertEquals(1, result.results().get(2).affectedRows());

			long aliceId = ((Number) result.results().get(0).returnedFields().get(CUSTOMER_ID_FIELD)).longValue();
			long bobId = ((Number) result.results().get(1).returnedFields().get(CUSTOMER_ID_FIELD)).longValue();
			long accountCustomerId = ((Number) result.results().get(2).returnedFields().get(ACCOUNT_CUSTOMER_ID_FIELD))
					.longValue();
			assertTrue(aliceId > 0);
			assertTrue(bobId > 0);
			assertEquals(bobId, accountCustomerId);

			assertEquals(beforeCustomers + 2, count(connection, "fg_correlation", "Customer"));
			assertEquals(beforeAccounts + 1, count(connection, "fg_correlation", "Account"));

			connection.rollback();
			assertEquals(beforeCustomers, count(connection, "fg_correlation", "Customer"));
			assertEquals(beforeAccounts, count(connection, "fg_correlation", "Account"));
		}
	}

	@Test
	void realPostgres_providerOwnedTransaction_commitsExactlyOnce() throws Exception {
		String url = jdbcUrl();
		Assumptions.assumeTrue(url != null, "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run PostgreSQL E2E tests.");

		try (Connection connection = DriverManager.getConnection(url)) {
			ensureCorrelationSchema(connection);
			connection.setAutoCommit(true);
			long before = count(connection, "fg_correlation", "Customer");

			MutationBatchResult result = new PostgresBatchedMutationExecutionProvider(connection, metadata(), true)
					.executeBatch(dependencyBatch(), ExecutionContext.EMPTY);

			assertEquals(3, result.results().size());
			assertEquals(before + 2, count(connection, "fg_correlation", "Customer"));

			// Cleanup in an explicit caller transaction. This avoids mutating the
			// initialized E2E database beyond the duration of the test.
			connection.setAutoCommit(false);
			try (PreparedStatement ps = connection
					.prepareStatement("DELETE FROM \"fg_correlation\".\"Account\" WHERE \"Name\" LIKE 'Java E2E %'")) {
				ps.executeUpdate();
			}
			try (PreparedStatement ps = connection
					.prepareStatement("DELETE FROM \"fg_correlation\".\"Customer\" WHERE \"Name\" LIKE 'Java E2E %'")) {
				ps.executeUpdate();
			}
			connection.commit();
			connection.setAutoCommit(true);
		}
	}

	@Test
	void cancelledToken_isRejectedBeforePostgresExecution() throws Exception {
		String url = jdbcUrl();
		Assumptions.assumeTrue(url != null, "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run PostgreSQL E2E tests.");

		try (Connection connection = DriverManager.getConnection(url)) {
			ensureCorrelationSchema(connection);
			connection.setAutoCommit(true);
			long before = count(connection, "fg_correlation", "Customer");
			try (CancellationTokenSource source = new CancellationTokenSource()) {
				source.cancel();
				assertThrows(java.util.concurrent.CancellationException.class,
						() -> new PostgresBatchedMutationExecutionProvider(connection, metadata(), true).executeBatch(
								ExecutionMutationIR.from(dependencyBatch()), ExecutionContext.EMPTY, source.token()));
			}
			assertEquals(before, count(connection, "fg_correlation", "Customer"));
		}
	}

	private static MutationBatchPlan dependencyBatch() {
		MutationEntitySchema customer = new MutationEntitySchema(CUSTOMER, "Customer",
				Set.of(CUSTOMER_ID, CUSTOMER_NAME),
				Map.of(CUSTOMER_ID_FIELD, CUSTOMER_ID, CUSTOMER_NAME_FIELD, CUSTOMER_NAME), CUSTOMER_ID);
		MutationEntitySchema account = new MutationEntitySchema(
				ACCOUNT, "Account", Set.of(ACCOUNT_ID, ACCOUNT_CUSTOMER_ID, ACCOUNT_NAME), Map.of(ACCOUNT_ID_FIELD,
						ACCOUNT_ID, ACCOUNT_CUSTOMER_ID_FIELD, ACCOUNT_CUSTOMER_ID, ACCOUNT_NAME_FIELD, ACCOUNT_NAME),
				ACCOUNT_ID);

		MutationOperation alice = new MutationOperation(customer, MutationKind.CREATE,
				List.of(new MutationFieldValue(CUSTOMER_NAME, unique("Java E2E Alice"))), null, null,
				List.of(CUSTOMER_ID_FIELD, CUSTOMER_NAME_FIELD));
		MutationOperation bob = new MutationOperation(customer, MutationKind.CREATE,
				List.of(new MutationFieldValue(CUSTOMER_NAME, unique("Java E2E Bob"))), null, null,
				List.of(CUSTOMER_ID_FIELD, CUSTOMER_NAME_FIELD));
		MutationOperation accountForBob = new MutationOperation(account, MutationKind.CREATE,
				List.of(MutationFieldValue.fromPrevious(ACCOUNT_CUSTOMER_ID, 1, CUSTOMER_ID_FIELD),
						new MutationFieldValue(ACCOUNT_NAME, unique("Java E2E Account"))),
				null, null, List.of(ACCOUNT_ID_FIELD, ACCOUNT_CUSTOMER_ID_FIELD, ACCOUNT_NAME_FIELD));

		return new MutationBatchPlan(List.of(alice, bob, accountForBob),
				List.of(new MutationDependency(1, 2, CUSTOMER_ID_FIELD, ACCOUNT_CUSTOMER_ID)));
	}

	private static String unique(String prefix) {
		return prefix + " " + UUID.randomUUID();
	}

	private static IMetadataProvider metadata() {
		MetadataRegistry registry = new MetadataRegistry();
		registry.register(new EntityMetadata(CUSTOMER, "Customer",
				List.of(new ColumnMetadata(CUSTOMER_ID, "Id"), new ColumnMetadata(CUSTOMER_NAME, "Name")),
				"fg_correlation.Customer",
				List.of(new FieldMetadata(CUSTOMER_ID_FIELD, "Id", Long.class,
						new ColumnReference(CUSTOMER, CUSTOMER_ID)),
						new FieldMetadata(CUSTOMER_NAME_FIELD, "Name", String.class,
								new ColumnReference(CUSTOMER, CUSTOMER_NAME))),
				new ColumnReference(CUSTOMER, CUSTOMER_ID), null, false, null, null));
		registry.register(new EntityMetadata(ACCOUNT, "Account", List.of(new ColumnMetadata(ACCOUNT_ID, "Id"),
				new ColumnMetadata(ACCOUNT_CUSTOMER_ID, "CustomerId"), new ColumnMetadata(ACCOUNT_NAME, "Name")),
				"fg_correlation.Account",
				List.of(new FieldMetadata(ACCOUNT_ID_FIELD, "Id", Long.class, new ColumnReference(ACCOUNT, ACCOUNT_ID)),
						new FieldMetadata(ACCOUNT_CUSTOMER_ID_FIELD, "CustomerId", Long.class,
								new ColumnReference(ACCOUNT, ACCOUNT_CUSTOMER_ID)),
						new FieldMetadata(ACCOUNT_NAME_FIELD, "Name", String.class,
								new ColumnReference(ACCOUNT, ACCOUNT_NAME))),
				new ColumnReference(ACCOUNT, ACCOUNT_ID), null, false, null, null));
		return registry;
	}

	private static void ensureCorrelationSchema(Connection connection) throws SQLException {
		try (Statement statement = connection.createStatement()) {
			statement.execute("CREATE SCHEMA IF NOT EXISTS \"fg_correlation\"");
			statement.execute(
					"CREATE TABLE IF NOT EXISTS \"fg_correlation\".\"Customer\" (\"Id\" BIGSERIAL PRIMARY KEY, \"Name\" TEXT NOT NULL)");
			statement.execute(
					"CREATE TABLE IF NOT EXISTS \"fg_correlation\".\"Account\" (\"Id\" BIGSERIAL PRIMARY KEY, \"CustomerId\" BIGINT NOT NULL REFERENCES \"fg_correlation\".\"Customer\"(\"Id\"), \"Name\" TEXT NOT NULL)");
		}
	}

	private static long count(Connection connection, String schema, String table) throws SQLException {
		try (PreparedStatement ps = connection
				.prepareStatement("SELECT COUNT(*) FROM \"" + schema + "\".\"" + table + "\"")) {
			try (ResultSet rs = ps.executeQuery()) {
				assertTrue(rs.next());
				return rs.getLong(1);
			}
		}
	}

	private static String jdbcUrl() {
		String configured = System.getenv("FOUNDGINE_POSTGRES_CONNECTION_STRING");
		if (configured == null || configured.isBlank())
			return null;
		if (configured.startsWith("jdbc:"))
			return configured;

		Map<String, String> parts = new HashMap<>();
		for (String item : configured.split(";")) {
			int eq = item.indexOf('=');
			if (eq > 0)
				parts.put(item.substring(0, eq).trim().toLowerCase(Locale.ROOT), item.substring(eq + 1).trim());
		}
		String host = parts.getOrDefault("host", "localhost");
		String port = parts.getOrDefault("port", "5432");
		String db = parts.getOrDefault("database", "postgres");
		String user = parts.getOrDefault("username", parts.getOrDefault("user", "postgres"));
		String password = parts.getOrDefault("password", "");
		return "jdbc:postgresql://" + host + ":" + port + "/" + db + "?user=" + encode(user) + "&password="
				+ encode(password);
	}

	private static String encode(String value) {
		return value.replace("%", "%25").replace("&", "%26").replace("?", "%3F").replace("=", "%3D");
	}
}
