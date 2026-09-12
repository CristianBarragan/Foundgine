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
 * Opt-in real PostgreSQL read-path parity coverage for the provider-neutral
 * Execution IR -> SqlCompiler -> JDBC execution boundary.
 *
 * <p>Set FOUNDGINE_POSTGRES_CONNECTION_STRING to a JDBC URL, for example
 * jdbc:postgresql://localhost:55432/foundgine_e2e?user=foundgine&password=foundgine.
 * The test creates an isolated schema and removes it in finally, so it does
 * not depend on the repository's seeded C# database.</p>
 */
class SqlExecutionPostgresE2ETest {
    private static final EntityId CUSTOMER = new EntityId(201);
    private static final EntityId ACCOUNT = new EntityId(202);
    private static final RelationshipId ACCOUNTS = new RelationshipId(203);

    private static final ColumnId CUSTOMER_ID_COLUMN = new ColumnId(201);
    private static final ColumnId CUSTOMER_NAME_COLUMN = new ColumnId(202);
    private static final ColumnId CUSTOMER_TENANT_COLUMN = new ColumnId(203);
    private static final ColumnId ACCOUNT_ID_COLUMN = new ColumnId(204);
    private static final ColumnId ACCOUNT_CUSTOMER_COLUMN = new ColumnId(205);
    private static final ColumnId ACCOUNT_BALANCE_COLUMN = new ColumnId(206);

    private static final FieldId CUSTOMER_ID = new FieldId(201);
    private static final FieldId CUSTOMER_NAME = new FieldId(202);
    private static final FieldId CUSTOMER_TENANT = new FieldId(203);
    private static final FieldId ACCOUNT_ID = new FieldId(204);
    private static final FieldId ACCOUNT_CUSTOMER = new FieldId(205);
    private static final FieldId ACCOUNT_BALANCE = new FieldId(206);

    @Test
    void realPostgres_executesAuthorizedFilteredRelationshipQuery_andMaterializesCells() throws Exception {
        String url = jdbcUrl();
        Assumptions.assumeTrue(url != null, "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run PostgreSQL E2E tests.");
        String schema = "fg_java_e2e_" + UUID.randomUUID().toString().replace("-", "");

        try (Connection connection = DriverManager.getConnection(url)) {
            createSchema(connection, schema);
            seed(connection, schema);

            MetadataRegistry metadata = metadata(schema);
            AuthorizationPredicate authorization = tenantAuthorization();
            SemanticRelationshipFilter relationshipFilter = new SemanticRelationshipFilter(
                    ACCOUNTS,
                    SemanticRelationshipQuantifier.SOME,
                    new SemanticFieldFilter(ACCOUNT_BALANCE, SemanticFilterOperator.GTE, 100L));

            SemanticQueryOptions options = new SemanticQueryOptions(
                    relationshipFilter, List.of(), 10, null, null);
            SemanticPlanNode root = new SemanticPlanNode(
                    1, ExecutionOperation.SCAN, CUSTOMER,
                    List.of(CUSTOMER_ID, CUSTOMER_NAME, CUSTOMER_TENANT),
                    null, null, List.of(), options, authorization, null,
                    RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT);
            SemanticPlan plan = new SemanticPlan(
                    root, List.of(),
                    new SemanticPlanAuthorizationBinding("postgres-e2e-contract", "postgres-e2e-auth"));

            SqlCompiler compiler = new SqlCompiler(metadata);
            SqlPlan sqlPlan = compiler.compile(plan);

            assertTrue(sqlPlan.commandText().contains("EXISTS (SELECT 1 FROM"), sqlPlan.commandText());
            assertTrue(sqlPlan.commandText().contains("@auth0"), sqlPlan.commandText());
            assertTrue(sqlPlan.commandText().contains("@p0"), sqlPlan.commandText());
            assertFalse(sqlPlan.commandText().contains("tenant-a"), sqlPlan.commandText());

            ExecutionContext context = new ExecutionContext(Map.of(
                    "tenant.id", "tenant-a",
                    ExecutionContextKeys.PAGINATION_LIMIT, 10));
            ExecutionResult result = new SqlExecutionProvider(connection).executeAsync(
                    sqlPlan, context, CancellationToken.NONE).toCompletableFuture().join();

            assertEquals(2, result.rows().size());
            assertEquals("Alice", result.rows().get(0).values().get("__fg_1_Name"));
            assertEquals("Carol", result.rows().get(1).values().get("__fg_1_Name"));
            assertEquals("tenant-a", result.rows().get(0).values().get("__fg_1_TenantId"));
            assertEquals(3, result.rows().get(0).cells().size());
            assertNotNull(result.evidence());
            assertEquals("sql", result.evidence().provider());
        } finally {
            dropSchema(url, schema);
        }
    }

    @Test
    void realPostgres_executesNestedRelationshipProjectionWithAuthorizationAndQuantifiers() throws Exception {
        String url = jdbcUrl();
        Assumptions.assumeTrue(url != null, "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run PostgreSQL E2E tests.");
        String schema = "fg_java_nested_" + UUID.randomUUID().toString().replace("-", "");

        try (Connection connection = DriverManager.getConnection(url)) {
            createSchema(connection, schema);
            seed(connection, schema);
            MetadataRegistry metadata = metadata(schema);

            SemanticQueryOptions options = new SemanticQueryOptions(
                    new SemanticAndFilter(List.of(
                            new SemanticRelationshipFilter(
                                    ACCOUNTS, SemanticRelationshipQuantifier.SOME,
                                    new SemanticFieldFilter(ACCOUNT_BALANCE, SemanticFilterOperator.GTE, 100L)),
                            new SemanticRelationshipFilter(
                                    ACCOUNTS, SemanticRelationshipQuantifier.NONE,
                                    new SemanticFieldFilter(ACCOUNT_BALANCE, SemanticFilterOperator.GT, 400L)))),
                    List.of(new SemanticOrderTerm(CUSTOMER_NAME, SemanticSortDirection.ASC)),
                    10, null, null);

            SemanticPlanNode child = new SemanticPlanNode(
                    2, ExecutionOperation.SCAN, ACCOUNT,
                    List.of(ACCOUNT_ID, ACCOUNT_BALANCE), ACCOUNTS, null, List.of(),
                    new SemanticQueryOptions(null, List.of(), null, null, null),
                    null, null, RelationshipTraversalMode.DEFAULT, 1, AggregateExecutionStrategy.DEFAULT);
            SemanticPlanNode root = new SemanticPlanNode(
                    1, ExecutionOperation.SCAN, CUSTOMER,
                    List.of(CUSTOMER_ID, CUSTOMER_NAME), null, null, List.of(child), options,
                    tenantAuthorization(), null, RelationshipTraversalMode.DEFAULT, -1,
                    AggregateExecutionStrategy.DEFAULT);

            SqlPlan sqlPlan = new SqlCompiler(metadata).compile(new SemanticPlan(
                    root, List.of(),
                    new SemanticPlanAuthorizationBinding("postgres-nested-contract", "postgres-nested-auth")));

            assertTrue(sqlPlan.commandText().contains("INNER JOIN"), sqlPlan.commandText());
            assertTrue(sqlPlan.commandText().contains("EXISTS (SELECT 1 FROM"), sqlPlan.commandText());
            assertTrue(sqlPlan.commandText().contains("NOT EXISTS"), sqlPlan.commandText());
            assertFalse(sqlPlan.commandText().contains("tenant-a"), sqlPlan.commandText());

            ExecutionResult result = new SqlExecutionProvider(connection).executeAsync(
                    sqlPlan,
                    new ExecutionContext(Map.of(
                            "tenant.id", "tenant-a",
                            ExecutionContextKeys.PAGINATION_LIMIT, 10)),
                    CancellationToken.NONE).toCompletableFuture().join();

            assertEquals(3, result.rows().size());
            assertEquals("Alice", result.rows().get(0).values().get("__fg_1_Name"));
            assertEquals("Carol", result.rows().get(1).values().get("__fg_1_Name"));
            assertEquals("Carol", result.rows().get(2).values().get("__fg_1_Name"));
            assertEquals(150L, ((Number) result.rows().get(0).values().get("__fg_2_Balance")).longValue());
            assertEquals(250L, ((Number) result.rows().get(1).values().get("__fg_2_Balance")).longValue());
            assertEquals(300L, ((Number) result.rows().get(2).values().get("__fg_2_Balance")).longValue());
            assertTrue(result.rows().get(0).cells().size() >= 4);
        } finally {
            dropSchema(url, schema);
        }
    }

    @Test
    void realPostgres_relationshipAllAndAggregateCountRemainFailClosedAndParameterized() throws Exception {
        String url = jdbcUrl();
        Assumptions.assumeTrue(url != null, "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run PostgreSQL E2E tests.");
        String schema = "fg_java_quantifiers_" + UUID.randomUUID().toString().replace("-", "");

        try (Connection connection = DriverManager.getConnection(url)) {
            createSchema(connection, schema);
            seed(connection, schema);
            MetadataRegistry metadata = metadata(schema);
            SqlCompiler compiler = new SqlCompiler(metadata);

            SemanticFilterExpression allBalancesAtLeast100 = new SemanticRelationshipFilter(
                    ACCOUNTS, SemanticRelationshipQuantifier.ALL,
                    new SemanticFieldFilter(ACCOUNT_BALANCE, SemanticFilterOperator.GTE, 100L));
            SemanticPlan allPlan = planWithFilter(allBalancesAtLeast100, 10);
            SqlPlan allSql = compiler.compile(allPlan);
            assertTrue(allSql.commandText().contains("NOT EXISTS"), allSql.commandText());
            assertFalse(allSql.commandText().contains("100"), allSql.commandText());

            ExecutionResult allResult = new SqlExecutionProvider(connection).executeAsync(
                    allSql,
                    new ExecutionContext(Map.of(ExecutionContextKeys.PAGINATION_LIMIT, 10)),
                    CancellationToken.NONE).toCompletableFuture().join();
            assertEquals(3, allResult.rows().size());

            SemanticAggregateFilter countAtLeastOne = new SemanticAggregateFilter(
                    ACCOUNTS, SemanticFilterAggregate.COUNT, null,
                    SemanticAggregateFilterOperator.GTE, 1L, null);
            SqlPlan countSql = compiler.compile(planWithFilter(countAtLeastOne, 10));
            assertTrue(countSql.commandText().contains("COUNT(*)"), countSql.commandText());
            assertFalse(countSql.commandText().contains("1L"), countSql.commandText());
            assertFalse(countSql.commandText().contains("> 1"), countSql.commandText());

            ExecutionResult countResult = new SqlExecutionProvider(connection).executeAsync(
                    countSql,
                    new ExecutionContext(Map.of(ExecutionContextKeys.PAGINATION_LIMIT, 10)),
                    CancellationToken.NONE).toCompletableFuture().join();
            assertEquals(3, countResult.rows().size());
        } finally {
            dropSchema(url, schema);
        }
    }

    private static SemanticPlan planWithFilter(SemanticFilterExpression filter, int limit) {
        SemanticQueryOptions options = new SemanticQueryOptions(
                filter, List.of(new SemanticOrderTerm(CUSTOMER_ID, SemanticSortDirection.ASC)), limit, null, null);
        SemanticPlanNode root = new SemanticPlanNode(
                1, ExecutionOperation.SCAN, CUSTOMER,
                List.of(CUSTOMER_ID, CUSTOMER_NAME), null, null, List.of(), options,
                null, null, RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT);
        return new SemanticPlan(root, List.of(),
                new SemanticPlanAuthorizationBinding("postgres-quantifier-contract", "postgres-quantifier-auth"));
    }

    @Test
    void realPostgres_cursorPagination_roundTripsEndCursorAndUsesStablePrimaryKeyTieBreak() throws Exception {
        String url = jdbcUrl();
        Assumptions.assumeTrue(url != null, "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run PostgreSQL E2E tests.");
        String schema = "fg_java_cursor_" + UUID.randomUUID().toString().replace("-", "");

        try (Connection connection = DriverManager.getConnection(url)) {
            createSchema(connection, schema);
            seedCursorData(connection, schema);
            MetadataRegistry metadata = metadata(schema);
            SqlCompiler compiler = new SqlCompiler(metadata);

            SqlPlan firstPlan = compiler.compile(cursorPlan(null));
            ExecutionContext context = new ExecutionContext(Map.of(
                    "tenant.id", "tenant-a",
                    ExecutionContextKeys.PAGINATION_LIMIT, 2));
            ExecutionResult first = new SqlExecutionProvider(connection).executeAsync(
                    firstPlan, context, CancellationToken.NONE).toCompletableFuture().join();

            assertEquals(2, first.rows().size());
            assertTrue(first.pageInfo().hasNextPage());
            assertNotNull(first.pageInfo().endCursor());

            SqlPlan secondPlan = compiler.compile(cursorPlan(first.pageInfo().endCursor()));
            ExecutionResult second = new SqlExecutionProvider(connection).executeAsync(
                    secondPlan, context, CancellationToken.NONE).toCompletableFuture().join();

            assertEquals(2, second.rows().size());
            assertEquals("Customer 3", second.rows().get(0).values().get("__fg_1_Name"));
            assertEquals("Customer 4", second.rows().get(1).values().get("__fg_1_Name"));
            assertFalse(second.pageInfo().hasNextPage());
        } finally {
            dropSchema(url, schema);
        }
    }

    @Test
    void realPostgres_aggregateCountOrdering_roundTripsCursorWithStableRootKeyTieBreak() throws Exception {
        String url = jdbcUrl();
        Assumptions.assumeTrue(url != null, "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run PostgreSQL E2E tests.");
        String schema = "fg_java_agg_cursor_" + UUID.randomUUID().toString().replace("-", "");

        try (Connection connection = DriverManager.getConnection(url)) {
            createSchema(connection, schema);
            seedAggregateCursorData(connection, schema);
            MetadataRegistry metadata = metadata(schema);
            SqlCompiler compiler = new SqlCompiler(metadata);

            SemanticPlan firstPlan = aggregateCursorPlan(null);
            assertTrue(firstPlan.root().queryOptions().effectiveOrder().get(0).isAggregate());
            SqlPlan firstSql = compiler.compile(firstPlan);
            assertTrue(firstSql.commandText().contains("COUNT(*)"), firstSql.commandText());
            assertTrue(firstSql.commandText().contains("ORDER BY"), firstSql.commandText());
            assertTrue(firstSql.commandText().contains("ASC"), firstSql.commandText());

            ExecutionContext context = new ExecutionContext(Map.of(
                    "tenant.id", "tenant-a",
                    ExecutionContextKeys.PAGINATION_LIMIT, 2));
            ExecutionResult first = new SqlExecutionProvider(connection).executeAsync(
                    firstSql, context, CancellationToken.NONE).toCompletableFuture().join();

            // tenant-a customers are Carol (2 accounts), Alice (1 account),
            // and Dave (0 accounts). Bob is tenant-b and is filtered out.
            assertEquals(2, first.rows().size());
            assertEquals("Carol", first.rows().get(0).values().get("__fg_1_Name"));
            assertEquals("Alice", first.rows().get(1).values().get("__fg_1_Name"));
            assertNotNull(first.pageInfo().endCursor());
            assertTrue(first.pageInfo().hasNextPage());

            SqlPlan secondSql = compiler.compile(aggregateCursorPlan(first.pageInfo().endCursor()));
            assertTrue(secondSql.commandText().contains("COUNT(*)"), secondSql.commandText());
            assertTrue(secondSql.commandText().contains("< @"), secondSql.commandText());

            ExecutionResult second = new SqlExecutionProvider(connection).executeAsync(
                    secondSql, context, CancellationToken.NONE).toCompletableFuture().join();

            assertEquals(1, second.rows().size());
            assertEquals("Dave", second.rows().get(0).values().get("__fg_1_Name"));
            assertFalse(second.pageInfo().hasNextPage());
        } finally {
            dropSchema(url, schema);
        }
    }

    private static SemanticPlan aggregateCursorPlan(String after) {
        SemanticQueryOptions options = new SemanticQueryOptions(
                null,
                List.of(new SemanticOrderTerm(ACCOUNT_ID, SemanticSortDirection.DESC,
                        List.of(ACCOUNTS), SemanticOrderAggregate.COUNT)),
                2, null, after);
        SemanticPlanNode root = new SemanticPlanNode(
                1, ExecutionOperation.SCAN, CUSTOMER,
                List.of(CUSTOMER_ID, CUSTOMER_NAME), null, null, List.of(), options,
                tenantAuthorization(), null, RelationshipTraversalMode.DEFAULT, -1,
                AggregateExecutionStrategy.DEFAULT);
        return new SemanticPlan(root, List.of(),
                new SemanticPlanAuthorizationBinding("postgres-aggregate-cursor-contract", "postgres-aggregate-cursor-auth"));
    }

    private static SemanticPlan cursorPlan(String after) {
        SemanticQueryOptions options = new SemanticQueryOptions(
                null,
                List.of(new SemanticOrderTerm(CUSTOMER_NAME, SemanticSortDirection.ASC)),
                2, null, after);
        SemanticPlanNode root = new SemanticPlanNode(
                1, ExecutionOperation.SCAN, CUSTOMER,
                List.of(CUSTOMER_ID, CUSTOMER_NAME), null, null, List.of(), options,
                AuthorizationPredicate.equal(
                        AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "TenantId"),
                        AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("tenant"), "id")),
                null, RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT);
        return new SemanticPlan(root, List.of(),
                new SemanticPlanAuthorizationBinding("postgres-cursor-contract", "postgres-cursor-auth"));
    }

    private static AuthorizationPredicate tenantAuthorization() {
        return AuthorizationPredicate.equal(
                AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "TenantId"),
                AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("tenant"), "id"));
    }

    private static MetadataRegistry metadata(String schema) {
        MetadataRegistry registry = new MetadataRegistry();
        registry.register(new EntityMetadata(
                CUSTOMER, "Customer",
                List.of(new ColumnMetadata(CUSTOMER_ID_COLUMN, "Id"),
                        new ColumnMetadata(CUSTOMER_NAME_COLUMN, "Name"),
                        new ColumnMetadata(CUSTOMER_TENANT_COLUMN, "TenantId")),
                schema + ".Customer",
                List.of(new FieldMetadata(CUSTOMER_ID, "Id", Long.class,
                                new ColumnReference(CUSTOMER, CUSTOMER_ID_COLUMN)),
                        new FieldMetadata(CUSTOMER_NAME, "Name", String.class,
                                new ColumnReference(CUSTOMER, CUSTOMER_NAME_COLUMN)),
                        new FieldMetadata(CUSTOMER_TENANT, "TenantId", String.class,
                                new ColumnReference(CUSTOMER, CUSTOMER_TENANT_COLUMN))),
                new ColumnReference(CUSTOMER, CUSTOMER_ID_COLUMN), null, false, null, null));
        registry.register(new EntityMetadata(
                ACCOUNT, "Account",
                List.of(new ColumnMetadata(ACCOUNT_ID_COLUMN, "Id"),
                        new ColumnMetadata(ACCOUNT_CUSTOMER_COLUMN, "CustomerId"),
                        new ColumnMetadata(ACCOUNT_BALANCE_COLUMN, "Balance")),
                schema + ".Account",
                List.of(new FieldMetadata(ACCOUNT_ID, "Id", Long.class,
                                new ColumnReference(ACCOUNT, ACCOUNT_ID_COLUMN)),
                        new FieldMetadata(ACCOUNT_CUSTOMER, "CustomerId", Long.class,
                                new ColumnReference(ACCOUNT, ACCOUNT_CUSTOMER_COLUMN)),
                        new FieldMetadata(ACCOUNT_BALANCE, "Balance", Long.class,
                                new ColumnReference(ACCOUNT, ACCOUNT_BALANCE_COLUMN))),
                new ColumnReference(ACCOUNT, ACCOUNT_ID_COLUMN), null, false, null, null));
        registry.register(new RelationshipMetadata(
                ACCOUNTS, CUSTOMER, ACCOUNT, "Accounts",
                new ColumnReference(CUSTOMER, CUSTOMER_ID_COLUMN),
                new ColumnReference(ACCOUNT, ACCOUNT_CUSTOMER_COLUMN), true, null));
        return registry;
    }

    private static void createSchema(Connection c, String schema) throws SQLException {
        try (Statement s = c.createStatement()) {
            s.execute("CREATE SCHEMA \"" + schema + "\"");
            s.execute("CREATE TABLE \"" + schema + "\".\"Customer\" (\"Id\" BIGSERIAL PRIMARY KEY, \"Name\" TEXT NOT NULL, \"TenantId\" TEXT NOT NULL)");
            s.execute("CREATE TABLE \"" + schema + "\".\"Account\" (\"Id\" BIGSERIAL PRIMARY KEY, \"CustomerId\" BIGINT NOT NULL REFERENCES \"" + schema + "\".\"Customer\"(\"Id\"), \"Balance\" BIGINT NOT NULL)");
        }
    }

    private static void seed(Connection c, String schema) throws SQLException {
        try (PreparedStatement customer = c.prepareStatement(
                "INSERT INTO \"" + schema + "\".\"Customer\" (\"Name\",\"TenantId\") VALUES (?,?)", Statement.RETURN_GENERATED_KEYS);
             PreparedStatement account = c.prepareStatement(
                     "INSERT INTO \"" + schema + "\".\"Account\" (\"CustomerId\",\"Balance\") VALUES (?,?)")) {
            long alice = insertCustomer(customer, "Alice", "tenant-a");
            long bob = insertCustomer(customer, "Bob", "tenant-b");
            long carol = insertCustomer(customer, "Carol", "tenant-a");
            insertAccount(account, alice, 150);
            insertAccount(account, bob, 500);
            insertAccount(account, carol, 250);
            insertAccount(account, carol, 300);
        }
    }

    private static void seedAggregateCursorData(Connection c, String schema) throws SQLException {
        try (PreparedStatement customer = c.prepareStatement(
                "INSERT INTO \"" + schema + "\".\"Customer\" (\"Name\",\"TenantId\") VALUES (?,?)", Statement.RETURN_GENERATED_KEYS);
             PreparedStatement account = c.prepareStatement(
                     "INSERT INTO \"" + schema + "\".\"Account\" (\"CustomerId\",\"Balance\") VALUES (?,?)")) {
            long alice = insertCustomer(customer, "Alice", "tenant-a");
            long bob = insertCustomer(customer, "Bob", "tenant-b");
            long carol = insertCustomer(customer, "Carol", "tenant-a");
            insertCustomer(customer, "Dave", "tenant-a");
            insertAccount(account, alice, 150);
            insertAccount(account, bob, 500);
            insertAccount(account, carol, 250);
            insertAccount(account, carol, 300);
        }
    }

    private static void seedCursorData(Connection c, String schema) throws SQLException {
        try (PreparedStatement customer = c.prepareStatement(
                "INSERT INTO \"" + schema + "\".\"Customer\" (\"Name\",\"TenantId\") VALUES (?,?)")) {
            for (int i = 1; i <= 4; i++) {
                customer.setString(1, "Customer " + i);
                customer.setString(2, "tenant-a");
                customer.executeUpdate();
            }
        }
    }

    private static long insertCustomer(PreparedStatement ps, String name, String tenant) throws SQLException {
        ps.setString(1, name);
        ps.setString(2, tenant);
        ps.executeUpdate();
        try (ResultSet rs = ps.getGeneratedKeys()) {
            assertTrue(rs.next());
            return rs.getLong(1);
        }
    }

    private static void insertAccount(PreparedStatement ps, long customerId, long balance) throws SQLException {
        ps.setLong(1, customerId);
        ps.setLong(2, balance);
        ps.executeUpdate();
    }

    private static String jdbcUrl() {
        String value = System.getenv("FOUNDGINE_POSTGRES_CONNECTION_STRING");
        if (value == null || value.isBlank()) return null;
        if (value.startsWith("jdbc:")) return value;
        return convertAdoStyle(value);
    }

    private static String convertAdoStyle(String value) {
        Map<String, String> parts = new LinkedHashMap<>();
        for (String item : value.split(";")) {
            int equals = item.indexOf('=');
            if (equals <= 0) continue;
            parts.put(item.substring(0, equals).trim().toLowerCase(Locale.ROOT), item.substring(equals + 1).trim());
        }
        String host = parts.getOrDefault("host", parts.getOrDefault("server", "localhost"));
        String port = parts.getOrDefault("port", "5432");
        String database = parts.getOrDefault("database", parts.getOrDefault("database name", "postgres"));
        String user = parts.getOrDefault("username", parts.getOrDefault("user id", "postgres"));
        String password = parts.getOrDefault("password", "");
        return "jdbc:postgresql://" + host + ":" + port + "/" + database + "?user=" + encode(user) + "&password=" + encode(password);
    }

    private static String encode(String value) {
        return value.replace("%", "%25").replace(" ", "%20").replace("&", "%26");
    }

    private static void dropSchema(String url, String schema) {
        if (url == null) return;
        try (Connection c = DriverManager.getConnection(url); Statement s = c.createStatement()) {
            s.execute("DROP SCHEMA IF EXISTS \"" + schema + "\" CASCADE");
        } catch (SQLException ignored) {
            // Preserve the original test failure if cleanup cannot connect.
        }
    }
}
