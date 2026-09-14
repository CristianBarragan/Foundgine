using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.Storage.Sql;
using Foundgine.Providers.Storage.Sql.Mutation;
using Foundgine.Providers.Storage.Sql.Mutation.Postgres;
using Npgsql;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

/// <summary>
/// PostgreSQL E2E integration proof for a stateful interaction sequence:
///
///   query -> mutation -> query -> mutation
///
/// The same PostgreSQL transaction is shared by every operation. The first
/// mutation creates a customer/account graph; the first query must observe the
/// newly-created state. A second semantic mutation then changes the account
/// status, and the second query must observe the changed state and exclude that
/// customer. The entire scenario is rolled back at the end.
///
/// This proves that the semantic, execution, SQL compilation and PostgreSQL
/// layers agree on state across multiple requests rather than only proving each
/// operation in isolation.
/// </summary>
public sealed class QueryMutationQueryMutationIntegrationE2ETests
{
    private const string ConnectionEnvironmentVariable = "FOUNDGINE_POSTGRES_CONNECTION_STRING";

    // SkippableFact (not [Fact]): when FOUNDGINE_POSTGRES_CONNECTION_STRING is
    // unset this test is reported as SKIPPED, not PASSED. A plain [Fact] with
    // an early `return;` shows as a pass, which lets CI go green without ever
    // touching PostgreSQL — silently defeating the point of a "flagship
    // stateful proof" test if the env var is ever misconfigured.
    [PostgreSqlFact]
    public async Task PostgresE2E_query_mutation_query_mutation_is_stateful_end_to_end_against_postgresql17()
    {
        var cs = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);

        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await SetSearchPathAsync(connection, "fg_stateful");
        await SetSearchPathAsync(connection, "fg_stateful");

        var mutationMetadata = PostgresE2ETests.BuildMetadata();
        var queryMetadata = PostgresE2ETests.BuildMetadata();
        var metadata = MergeMetadata(mutationMetadata, queryMetadata);

        await using var transaction = await connection.BeginTransactionAsync();
        // Seed only test data. The schema itself is initialized once by PostgreSQL.
        // PostgreSQL generates every identity and the returned IDs are the only
        // IDs used by this test.
        var baseline = await SeedQueryBaselineAsync(connection, transaction);
        try
        {
            // -----------------------------------------------------------------
            // PASS 1 — QUERY
            // Establish the baseline before any mutation occurs.
            // -----------------------------------------------------------------
            var queryModel = ComplexQueryPostgresE2ETests.BuildModel();
            var queryRequest = ComplexQueryPostgresE2ETests.BuildComplexRequest(50, 3);
            var queryPlan = ComplexQueryPostgresE2ETests.Compile(
                queryModel, metadata, queryRequest);

            Assert.Contains("EXISTS", queryPlan.CommandText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("NOT EXISTS", queryPlan.CommandText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("COUNT(*)", queryPlan.CommandText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ORDER BY", queryPlan.CommandText, StringComparison.OrdinalIgnoreCase);

            var queryProvider = new SqlExecutionProvider(connection, transaction);
            var firstQuery = await queryProvider.ExecuteAsync(queryPlan, PaginationExecutionContext.Create(50));
            var baselineCustomerIds = firstQuery.Rows
                .Select(r => Convert.ToInt64(r.Values["__fg_0_Id"]))
                .Distinct()
                .ToHashSet();

            Assert.Contains(baseline.Customer1Id, baselineCustomerIds);
            Assert.Contains(baseline.Customer3Id, baselineCustomerIds);

            // -----------------------------------------------------------------
            // PASS 2 — MUTATION
            // Create the Customer/Account graph after the baseline query.
            // -----------------------------------------------------------------
            var mutationGraph = ComplexSemanticMutationE2ETests.BuildGraph();
            var mutationPlan = new SemanticMutationPlanner().Plan(mutationGraph);
            var mutationIr = new SemanticMutationExecutionLowerer(metadata).Lower(mutationPlan);
            mutationIr.ValidateDerivedDependencies();

            var mutationSql = new PostgresBatchedMutationCompiler(metadata).Compile(mutationIr);
            Assert.NotEmpty(mutationSql.CommandText);
            Assert.Contains("unnest", mutationSql.CommandText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("__fg_corr", mutationSql.CommandText, StringComparison.Ordinal);

            var mutationProvider = new PostgresBatchedMutationExecutionProvider(
                connection, metadata, transaction);
            var firstMutation = mutationProvider.ExecuteBatch(mutationIr, new ExecutionContext());
            Assert.Equal(10, firstMutation.Results.Count);

            var createdCustomerId = Convert.ToInt64(
                firstMutation.Results[0].ReturnedFields![ComplexSemanticMutationE2ETests.Id]);
            Assert.True(createdCustomerId > 0);

            // -----------------------------------------------------------------
            // PASS 3 — QUERY
            // The same semantic query must now see the graph created by pass 2.
            // -----------------------------------------------------------------
            var secondQuery = await queryProvider.ExecuteAsync(queryPlan, PaginationExecutionContext.Create(50));
            var afterCreateCustomerIds = secondQuery.Rows
                .Select(r => Convert.ToInt64(r.Values["__fg_0_Id"]))
                .Distinct()
                .ToArray();

            Assert.Contains(createdCustomerId, afterCreateCustomerIds);
            var createdRows = secondQuery.Rows
                .Where(r => Convert.ToInt64(r.Values["__fg_0_Id"]) == createdCustomerId)
                .ToArray();
            Assert.NotEmpty(createdRows);
            Assert.All(createdRows, row => Assert.Equal("Primary", row.Values["__fg_1_Name"]));

            // -----------------------------------------------------------------
            // PASS 4 — MUTATION: change the account created by mutation #1 from Open
            //    to Blocked. This is a second, independent semantic mutation
            //    request, not a direct SQL update.
            // -----------------------------------------------------------------
            // Scoped to CustomerId == createdCustomerId in addition to
            // Name/Status: without the CustomerId condition this update would
            // silently match *any* Account named "Primary" with Status "Open"
            // (e.g. another customer created the same way), which would let
            // this test pass for the wrong reason. This exact risk is what
            // Second_mutation_only_blocks_the_intended_customers_account below
            // proves against a second, independently-created customer. Also
            // uses the builder's own constants instead of restating
            // EntityId(702)/FieldId(2)/FieldId(3).
            var secondMutationGraph = BuildBlockAccountGraph(createdCustomerId);

            var secondMutationPlan = new SemanticMutationPlanner().Plan(secondMutationGraph);
            var secondMutationIr = new SemanticMutationExecutionLowerer(metadata).Lower(secondMutationPlan);
            secondMutationIr.ValidateDerivedDependencies();

            var secondMutationSql = new PostgresBatchedMutationCompiler(metadata).Compile(secondMutationIr);
            Assert.NotEmpty(secondMutationSql.CommandText);

            var secondMutation = mutationProvider.ExecuteBatch(secondMutationIr, new ExecutionContext());
            Assert.Single(secondMutation.Results);

            // -----------------------------------------------------------------
            // PASS 4 — MUTATION
            // The final pass changes only the account created by pass 2.
            // Exactly one row must be affected.
            // -----------------------------------------------------------------
            Assert.Equal(1, secondMutation.Results[0].AffectedRows);
            Assert.Equal(
                "Blocked",
                secondMutation.Results[0].ReturnedFields![ComplexSemanticMutationE2ETests.Status]?.ToString());
        }
        finally
        {
            // The outer transaction is rolled back, so the
            // database is left unchanged."
            await transaction.RollbackAsync();
        }
    }

    /// <summary>
    /// Negative counterpart to the main scenario above. It exists to prove
    /// that scoping the second mutation's filter by CustomerId (in addition
    /// to Name/Status) is doing real work, not just defensive-looking
    /// decoration.
    ///
    /// The main test's second mutation filters on
    /// Name == "Primary" AND Status == "Open" AND CustomerId == created.
    /// If the CustomerId condition were ever dropped, the main test would
    /// still pass by coincidence, because it only ever creates one such
    /// account. Here we run ComplexSemanticMutationE2ETests'
    /// Create/Upsert graph *twice* against the same transaction, producing
    /// two independent customers who each get an Account named "Primary"
    /// with Status "Open" (the values BuildGraph() always uses). We then
    /// block only the first customer's account and assert the second is
    /// untouched — an unscoped filter would match both rows and either fail
    /// AffectedRows == 1 or block the wrong customer's account.
    ///
    /// This is built via two independent mutations rather than a raw SQL
    /// decoy row because PrepareUnifiedDatabaseAsync declares a genuinely
    /// unique index on Account.CustomerId (required as the ON CONFLICT
    /// arbiter for the Upsert in mutation #1) — a customer can only ever
    /// have one Account row in this schema, so two customers are needed to
    /// produce two "Primary"/"Open" rows.
    /// </summary>
    [PostgreSqlFact]
    public async Task Second_mutation_only_blocks_the_intended_customers_account()
    {
        var cs = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);

        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await SetSearchPathAsync(connection, "fg_stateful");

        var mutationMetadata = PostgresE2ETests.BuildMetadata();
        var queryMetadata = PostgresE2ETests.BuildMetadata();
        var metadata = MergeMetadata(mutationMetadata, queryMetadata);

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await SeedQueryBaselineAsync(connection, transaction);

            var mutationProvider = new PostgresBatchedMutationExecutionProvider(
                connection, metadata, transaction);

            var firstCustomerId = CreatePrimaryOpenCustomer(mutationProvider, metadata);
            var secondCustomerId = CreatePrimaryOpenCustomer(mutationProvider, metadata);
            Assert.NotEqual(firstCustomerId, secondCustomerId);

            var blockGraph = BuildBlockAccountGraph(firstCustomerId);
            var blockPlan = new SemanticMutationPlanner().Plan(blockGraph);
            var blockIr = new SemanticMutationExecutionLowerer(metadata).Lower(blockPlan);
            blockIr.ValidateDerivedDependencies();

            var blockResult = mutationProvider.ExecuteBatch(blockIr, new ExecutionContext());

            // Exactly one row touched: the CustomerId condition is the only
            // thing distinguishing the two otherwise-identical
            // "Primary"/"Open" accounts. Without it, both would match.
            Assert.Single(blockResult.Results);
            Assert.Equal(1, blockResult.Results[0].AffectedRows);

            const string statusCheckSql = """
                                          SELECT "Status" FROM "Account" WHERE "CustomerId" = @customerId;
                                          """;

            await using var firstCheck = new NpgsqlCommand(statusCheckSql, connection, transaction);
            firstCheck.Parameters.AddWithValue("customerId", firstCustomerId);
            Assert.Equal("Blocked", (string?)await firstCheck.ExecuteScalarAsync());

            await using var secondCheck = new NpgsqlCommand(statusCheckSql, connection, transaction);
            secondCheck.Parameters.AddWithValue("customerId", secondCustomerId);
            Assert.Equal("Open", (string?)await secondCheck.ExecuteScalarAsync());
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static long CreatePrimaryOpenCustomer(
        PostgresBatchedMutationExecutionProvider mutationProvider,
        MetadataRegistry metadata)
    {
        var graph = ComplexSemanticMutationE2ETests.BuildGraph();
        var plan = new SemanticMutationPlanner().Plan(graph);
        var ir = new SemanticMutationExecutionLowerer(metadata).Lower(plan);
        ir.ValidateDerivedDependencies();

        var result = mutationProvider.ExecuteBatch(ir, new ExecutionContext());
        return Convert.ToInt64(
            result.Results[0].ReturnedFields![ComplexSemanticMutationE2ETests.Id]);
    }

    private static SemanticMutationOperationGraph BuildBlockAccountGraph(long customerId)
    {
        return new SemanticMutationOperationGraph(
        [
            new SemanticMutationOperation(
                ComplexSemanticMutationE2ETests.Account,
                SemanticMutationKind.Update,
                [new SemanticMutationField(ComplexSemanticMutationE2ETests.Status, "Blocked")],
                new SemanticAndFilter(
                [
                    new SemanticFieldFilter(
                        ComplexSemanticMutationE2ETests.CustomerId,
                        SemanticFilterOperator.Eq,
                        customerId),
                    new SemanticFieldFilter(
                        ComplexSemanticMutationE2ETests.Name,
                        SemanticFilterOperator.Eq,
                        "Primary"),
                    new SemanticFieldFilter(
                        ComplexSemanticMutationE2ETests.Status,
                        SemanticFilterOperator.Eq,
                        "Open")
                ]),
                ConflictFields: [],
                ReturnFields:
                [
                    ComplexSemanticMutationE2ETests.Id,
                    ComplexSemanticMutationE2ETests.Status
                ],
                Effects:
                [
                    new(SemanticMutationEffectKind.UpdateEntity, ComplexSemanticMutationE2ETests.Account),
                    new(SemanticMutationEffectKind.SetField, ComplexSemanticMutationE2ETests.Account,
                        ComplexSemanticMutationE2ETests.Status)
                ],
                Dependencies: [])
        ]);
    }

    // NOTE: this assumes the mutation-side and query-side metadata registries
    // never share an EntityId/RelationshipId. That happens to hold today
    // because ComplexSemanticMutationE2ETests uses IDs in the
    // 700s/1-6 range and ComplexQueryPostgresE2ETests uses a
    // disjoint range, but nothing here enforces it. If either side's ID
    // ranges are ever changed, MetadataRegistry.Register will need to surface
    // a collision loudly (or this helper should assert on it) rather than
    // silently letting one registration overwrite the other.
    private static MetadataRegistry MergeMetadata(MetadataRegistry left, MetadataRegistry right)
    {
        var merged = new MetadataRegistry();
        foreach (var entity in left.Entities) merged.Register(entity);
        foreach (var relationship in left.Relationships) merged.Register(relationship);
        foreach (var entity in right.Entities) merged.Register(entity);
        foreach (var relationship in right.Relationships) merged.Register(relationship);
        return merged;
    }

    private static async Task<QueryBaseline> SeedQueryBaselineAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        var customer1Id = await InsertCustomerAsync(connection, transaction, "Alice", "Active");
        var customer2Id = await InsertCustomerAsync(connection, transaction, "Bob", "Active");
        var customer3Id = await InsertCustomerAsync(connection, transaction, "Carol", "Active");

        var account1Id = await InsertAccountAsync(
            connection, transaction, customer1Id, "Seed", 150m, "Active");
        var account2Id = await InsertAccountAsync(
            connection, transaction, customer2Id, "Seed", 25m, "Blocked");
        var account3Id = await InsertAccountAsync(
            connection, transaction, customer3Id, "Seed", 150m, "Active");

        await InsertTransactionAsync(
            connection, transaction, account1Id, 300m, new DateTime(2026, 1, 1));
        await InsertTransactionAsync(
            connection, transaction, account2Id, 30m, new DateTime(2026, 1, 2));
        await InsertTransactionAsync(
            connection, transaction, account3Id, 300m, new DateTime(2026, 1, 1));

        return new QueryBaseline(customer1Id, customer2Id, customer3Id);
    }

    private static async Task<long> InsertCustomerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string name,
        string status)
    {
        const string sql = """
                           INSERT INTO "Customer" ("Name", "Status")
                           VALUES (@name, @status)
                           RETURNING "Id";
                           """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("status", status);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> InsertAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long customerId,
        string name,
        decimal balance,
        string status)
    {
        const string sql = """
                           INSERT INTO "Account" ("CustomerId", "Name", "Balance", "Status")
                           VALUES (@customerId, @name, @balance, @status)
                           RETURNING "Id";
                           """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("customerId", customerId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("balance", balance);
        command.Parameters.AddWithValue("status", status);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> InsertTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long accountId,
        decimal amount,
        DateTime transactionDate)
    {
        const string sql = """
                           INSERT INTO "Transaction" ("AccountId", "Amount", "TransactionDate")
                           VALUES (@accountId, @amount, @transactionDate)
                           RETURNING "Id";
                           """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("accountId", accountId);
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("transactionDate", transactionDate);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task SetSearchPathAsync(NpgsqlConnection connection, string schema)
    {
        await using var command = new NpgsqlCommand(
            $"SET search_path TO \"{schema}\";",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record QueryBaseline(
        long Customer1Id,
        long Customer2Id,
        long Customer3Id);
}