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
/// The same PostgreSQL transaction is shared by every pass, and everything is
/// rolled back at the end.
///
///   1. Query:    baseline over seeded data (before any mutation).
///   2. Mutation: ComplexSemanticMutationE2ETests.BuildGraph() creates a
///                Customer/Account/... graph.
///   3. Query:    the same query plan must now observe the created customer
///                (after one fixture Transaction row is added, because the
///                query INNER JOINs transactions and BuildGraph() creates none).
///   4. Mutation: a second, independent semantic mutation blocks that
///                customer's account. This is verified through the
///                mutation's own result (AffectedRows and the returned
///                Status); there is no further query after pass 4.
///
/// Note on account status: BuildGraph() upserts the Account with Status
/// "Open", but its operation 8 then sets Status to "Active" for accounts
/// that have at least one order (operation 4 creates one). Because
/// BuildGraph() mixes Create/Upsert and Update on the same entities, the
/// Postgres provider executes it through the sequential fallback (see
/// PostgresBatchedMutationCompiler.TryCompile), so operation 8 does see the
/// order and the account ends up "Active". Pass 4 therefore filters on
/// "Active", not "Open".
///
/// This proves that the semantic, execution, SQL compilation and PostgreSQL
/// layers agree on state across multiple requests rather than only proving each
/// operation in isolation.
/// </summary>
public sealed class QueryMutationQueryMutationIntegrationE2ETests
{
    private const string ConnectionEnvironmentVariable = "FOUNDGINE_POSTGRES_CONNECTION_STRING";

    // [PostgreSqlFact] (not a plain [Fact]): when
    // FOUNDGINE_POSTGRES_CONNECTION_STRING is unset the test is reported as
    // SKIPPED, not PASSED. A plain [Fact] with an early `return;` shows as a
    // pass, which would let CI go green without ever touching PostgreSQL.
    [PostgreSqlFact]
    public async Task PostgresE2E_query_mutation_query_mutation_is_stateful_end_to_end_against_postgresql17()
    {
        var cs = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);

        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await SetSearchPathAsync(connection, "fg_stateful");

        var mutationMetadata = PostgresE2ETests.BuildMetadata();
        var queryMetadata = ComplexQueryPostgresE2ETests.BuildMetadata();
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

            // Compile() is called directly only to inspect the single-statement
            // batched SQL shape. ExecuteBatch below uses TryCompile(), which
            // falls back to sequential execution for this graph (see class
            // summary), so this SQL is not what actually runs.
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

            // Operation 2 of BuildGraph() is the Account upsert and returns Id.
            var createdAccountId = Convert.ToInt64(
                firstMutation.Results[2].ReturnedFields![ComplexSemanticMutationE2ETests.Id]);
            Assert.True(createdAccountId > 0);

            // The query below INNER JOINs Account -> Transaction and only
            // matches customers with a 100/250-balance account or a
            // 250-amount transaction. BuildGraph() creates no Transaction and
            // the Account keeps the column default Balance (150), so the new
            // customer is not yet visible to that query. Add the one fixture
            // row that makes it visible; the Customer/Account rows it joins
            // to were created by the mutation above, not by SQL.
            await InsertTransactionAsync(
                connection, transaction, createdAccountId, 250m, new DateTime(2026, 1, 3));

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
            // The query selects Account Id/Balance (not Name), so identify the
            // account by Id.
            Assert.All(createdRows, row =>
                Assert.Equal(createdAccountId, Convert.ToInt64(row.Values["__fg_1_Id"])));

            // -----------------------------------------------------------------
            // PASS 4 — MUTATION
            // Block the account created in pass 2 (Active -> Blocked) with a
            // second, independent semantic mutation request, not a direct SQL
            // update.
            //
            // The filter is scoped by CustomerId in addition to Name/Status.
            // Without the CustomerId condition it would match *any* "Primary"
            // /"Active" Account (e.g. one belonging to another customer
            // created the same way), and this test could pass for the wrong
            // reason. That risk is exercised directly by
            // Second_mutation_only_blocks_the_intended_customers_account
            // below. BuildBlockAccountGraph also uses the builder's own
            // constants instead of restating EntityId(702)/FieldId(2)/
            // FieldId(3).
            // -----------------------------------------------------------------
            var secondMutationGraph = BuildBlockAccountGraph(createdCustomerId);

            var secondMutationPlan = new SemanticMutationPlanner().Plan(secondMutationGraph);
            var secondMutationIr = new SemanticMutationExecutionLowerer(metadata).Lower(secondMutationPlan);
            secondMutationIr.ValidateDerivedDependencies();

            var secondMutationSql = new PostgresBatchedMutationCompiler(metadata).Compile(secondMutationIr);
            Assert.NotEmpty(secondMutationSql.CommandText);

            var secondMutation = mutationProvider.ExecuteBatch(secondMutationIr, new ExecutionContext());
            Assert.Single(secondMutation.Results);

            // Exactly one row must be affected, and it must be the account
            // created in pass 2.
            Assert.Equal(1, secondMutation.Results[0].AffectedRows);
            Assert.Equal(
                "Blocked",
                secondMutation.Results[0].ReturnedFields![ComplexSemanticMutationE2ETests.Status]?.ToString());
        }
        finally
        {
            // The outer transaction is rolled back, so the database is left
            // unchanged.
            await transaction.RollbackAsync();
        }
    }

    /// <summary>
    /// Negative counterpart to the main scenario above. It exists to prove
    /// that scoping the block mutation's filter by CustomerId (in addition
    /// to Name/Status) is doing real work, not just defensive-looking
    /// decoration.
    ///
    /// The block mutation filters on
    /// Name == "Primary" AND Status == "Active" AND CustomerId == target.
    /// If the CustomerId condition were ever dropped, the main test would
    /// still pass by coincidence, because it only ever creates one such
    /// account. Here we run ComplexSemanticMutationE2ETests.BuildGraph()
    /// *twice* against the same transaction, producing two independent
    /// customers who each end up with an Account named "Primary" with
    /// Status "Active". (BuildGraph() upserts the Account as "Open", then
    /// its operation 8 flips it to "Active" because the graph also creates an
    /// order for it.) We then block only the first customer's account and
    /// assert the second is untouched — an unscoped filter would match both
    /// rows and either fail AffectedRows == 1 or block the wrong customer's
    /// account.
    ///
    /// This is built via two independent mutations rather than a raw SQL
    /// decoy row because PrepareUnifiedDatabaseAsync declares a genuinely
    /// unique index on Account.CustomerId (required as the ON CONFLICT
    /// arbiter for the Account Upsert in BuildGraph()) — a customer can only
    /// ever have one Account row in this schema, so two customers are needed
    /// to produce two "Primary"/"Active" rows.
    /// </summary>
    [PostgreSqlFact]
    public async Task Second_mutation_only_blocks_the_intended_customers_account()
    {
        var cs = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);

        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await SetSearchPathAsync(connection, "fg_stateful");

        var mutationMetadata = PostgresE2ETests.BuildMetadata();
        var queryMetadata = ComplexQueryPostgresE2ETests.BuildMetadata();
        var metadata = MergeMetadata(mutationMetadata, queryMetadata);

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await SeedQueryBaselineAsync(connection, transaction);

            var mutationProvider = new PostgresBatchedMutationExecutionProvider(
                connection, metadata, transaction);

            var firstCustomerId = CreatePrimaryActiveCustomer(mutationProvider, metadata);
            var secondCustomerId = CreatePrimaryActiveCustomer(mutationProvider, metadata);
            Assert.NotEqual(firstCustomerId, secondCustomerId);

            var blockGraph = BuildBlockAccountGraph(firstCustomerId);
            var blockPlan = new SemanticMutationPlanner().Plan(blockGraph);
            var blockIr = new SemanticMutationExecutionLowerer(metadata).Lower(blockPlan);
            blockIr.ValidateDerivedDependencies();

            var blockResult = mutationProvider.ExecuteBatch(blockIr, new ExecutionContext());

            // Exactly one row touched: the CustomerId condition is the only
            // thing distinguishing the two otherwise-identical
            // "Primary"/"Active" accounts. Without it, both would match.
            Assert.Single(blockResult.Results);
            Assert.Equal(1, blockResult.Results[0].AffectedRows);

            const string statusCheckSql = """
                                          SELECT "Status" FROM "Account" WHERE "CustomerId" = @customerId;
                                          """;

            await using var firstCheck = new NpgsqlCommand(statusCheckSql, connection, transaction);
            firstCheck.Parameters.AddWithValue("customerId", firstCustomerId);
            Assert.Equal("Blocked", (string?)await firstCheck.ExecuteScalarAsync());

            // The second customer's account keeps the status BuildGraph()
            // left it in ("Active"); it must not have been blocked.
            await using var secondCheck = new NpgsqlCommand(statusCheckSql, connection, transaction);
            secondCheck.Parameters.AddWithValue("customerId", secondCustomerId);
            Assert.Equal("Active", (string?)await secondCheck.ExecuteScalarAsync());
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    /// <summary>
    /// Runs ComplexSemanticMutationE2ETests.BuildGraph() and returns the
    /// created customer's id. The customer's Account ends up as
    /// "Primary"/"Active" (see the class summary).
    /// </summary>
    private static long CreatePrimaryActiveCustomer(
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

    /// <summary>
    /// Update graph that sets Status = "Blocked" on the Account where
    /// CustomerId == <paramref name="customerId"/>, Name == "Primary" and
    /// Status == "Active". "Active" is the status BuildGraph() leaves the
    /// account in, not the "Open" it is first upserted with.
    /// </summary>
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
                        "Active")
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

        // Balance 250 on the "included" accounts: the shared complex query
        // (ComplexQueryPostgresE2ETests.BuildComplexRequest) only matches
        // customers that have an account with Balance IN (100, 250) or a
        // transaction with Amount 250. Bob's Blocked account is excluded by
        // the "all accounts != Blocked" predicate.
        var account1Id = await InsertAccountAsync(
            connection, transaction, customer1Id, "Seed", 250m, "Active");
        var account2Id = await InsertAccountAsync(
            connection, transaction, customer2Id, "Seed", 25m, "Blocked");
        var account3Id = await InsertAccountAsync(
            connection, transaction, customer3Id, "Seed", 250m, "Active");

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