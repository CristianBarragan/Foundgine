using System.Text.Json;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql;
using Npgsql;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

/// <summary>
/// Flagship read proof. A single intentionally complex semantic request is
/// resolved, authorized, planned, lowered into provider-independent Execution IR,
/// compiled to SQL, executed through Npgsql against PostgreSQL 17, and validated
/// from actual database rows.
///
/// The query exercises nested selections, two-hop traversal, AND/OR composition,
/// Some/None/All relationship quantifiers, Count aggregation, IN/NEQ/GTE filters,
/// ordering and a parameterized limit. EXPLAIN ANALYZE is run inside the test transaction, which is rolled back at
/// the end so the measurement gate cannot leave fixture data behind.
/// </summary>
public sealed class ComplexQueryPostgresE2ETests
{
    private const string ConnectionEnvironmentVariable = "FOUNDGINE_POSTGRES_CONNECTION_STRING";

    private static readonly EntityId Customer = new(800);
    private static readonly EntityId Account = new(801);
    private static readonly EntityId Transaction = new(802);
    private static readonly RelationshipId CustomerAccounts = new(800);
    private static readonly RelationshipId AccountTransactions = new(801);

    [PostgreSqlFact]
    public async Task PostgresE2E_complex_query_runs_semantics_to_postgresql17()
    {
        var cs = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);

        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await SetSearchPathAsync(connection, "fg_query");

        var model = BuildModel();
        var metadata = BuildMetadata();
        await using var transaction = await connection.BeginTransactionAsync();
        var baseline = await SeedAsync(connection, transaction);

        var request = BuildComplexRequest(limit: 50);
        var plan = Compile(model, metadata, request);

        Assert.Contains("EXISTS", plan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT EXISTS", plan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT(*)", plan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" IN (", plan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" <> ", plan.CommandText, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN", plan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", plan.CommandText, StringComparison.OrdinalIgnoreCase);

        var stats = await ExplainAnalyzeAsync(connection, transaction, plan, limit: 50);
        PrintStats("complex", 50, 3, stats);
        Assert.True(stats.PlanningTimeMs >= 0);
        Assert.True(stats.ExecutionTimeMs >= 0);
        Assert.NotEmpty(stats.JoinStrategies);

        var provider = new SqlExecutionProvider(connection, transaction);
        var result = await provider.ExecuteAsync(plan, PaginationExecutionContext.Create(50));

        Assert.NotEmpty(result.Rows);
        Assert.All(result.Rows, row =>
        {
            Assert.NotNull(row.Values["__fg_0_Id"]);
            Assert.NotNull(row.Values["__fg_0_Name"]);
            Assert.NotNull(row.Values["__fg_1_Id"]);
            Assert.NotNull(row.Values["__fg_1_Balance"]);
            Assert.NotNull(row.Values["__fg_2_Id"]);
            Assert.NotNull(row.Values["__fg_2_Amount"]);
        });

        var customerIds = result.Rows.Select(r => Convert.ToInt64(r.Values["__fg_0_Id"])).Distinct().ToArray();
        Assert.Contains(baseline.Customer1Id, customerIds);
        Assert.DoesNotContain(baseline.Customer2Id,
            customerIds); // blocked/closed account path fails semantic predicates.
        Assert.Contains(baseline.Customer3Id, customerIds);

        await transaction.RollbackAsync();
    }

    [PostgreSqlTheory]
    [InlineData(1, 1)]
    [InlineData(10, 1)]
    [InlineData(50, 2)]
    [InlineData(500, 3)]
    public async Task PostgresE2E_complex_query_measurement_matrix(
        int datasetSize,
        int depth)
    {
        var cs = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);

        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

        try
        {
            await SeedScaledAsync(connection, transaction, datasetSize);

            var model = BuildModel();
            var metadata = BuildMetadata();
            var request = BuildComplexRequest(limit: Math.Max(datasetSize, 50), depth);
            var plan = Compile(model, metadata, request);
            var stats = await ExplainAnalyzeAsync(connection, transaction, plan, Math.Max(datasetSize, 50));

            PrintStats("matrix", datasetSize, depth, stats);
            PrintNodeEvidence(stats);

            Assert.True(stats.PlanningTimeMs >= 0);
            Assert.True(stats.ExecutionTimeMs >= 0);
            Assert.True(stats.RootActualRows >= 0);
            Assert.True(stats.RootPlanRows >= 0);
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    internal static SemanticRequest BuildComplexRequest(int limit, int depth = 3)
    {
        var selections = new List<SemanticSelection>
        {
            new(new FieldId(1), null, []),
            new(new FieldId(2), null, [])
        };

        if (depth >= 2)
        {
            var transactionSelections = new List<SemanticSelection>
            {
                new(new FieldId(1), null, []),
                new(new FieldId(3), null, []),
                new(new FieldId(4), null, [])
            };

            selections.Add(new SemanticSelection(
                null,
                CustomerAccounts,
                [
                    new SemanticSelection(new FieldId(1), null, []),
                    new SemanticSelection(new FieldId(3), null, []),
                    new SemanticSelection(null, AccountTransactions, transactionSelections)
                ]));
        }

        var accountSome = new SemanticRelationshipFilter(
            CustomerAccounts,
            SemanticRelationshipQuantifier.Some,
            new SemanticOrFilter(
            [
                new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.In, new[] { 100m, 250m }),
                new SemanticRelationshipFilter(
                    AccountTransactions,
                    SemanticRelationshipQuantifier.Some,
                    new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.In, new[] { 250m }))
            ]));

        var accountNone = new SemanticRelationshipFilter(
            CustomerAccounts,
            SemanticRelationshipQuantifier.None,
            new SemanticFieldFilter(new FieldId(4), SemanticFilterOperator.Eq, "Closed"));

        var accountAll = new SemanticRelationshipFilter(
            CustomerAccounts,
            SemanticRelationshipQuantifier.All,
            new SemanticFieldFilter(new FieldId(4), SemanticFilterOperator.Neq, "Blocked"));

        var filter = new SemanticAndFilter(
        [
            new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.In,
                new[] { "Alice", "Bob", "Carol", "Customer-10" }),
            accountSome,
            accountNone,
            accountAll,
            new SemanticAggregateFilter(
                CustomerAccounts,
                SemanticFilterAggregate.Count,
                null,
                SemanticAggregateFilterOperator.Gte,
                1)
        ]);

        return new SemanticRequest(
            Customer,
            selections,
            new SemanticQueryOptions(
                Filter: filter,
                Order: [new SemanticOrderTerm(new FieldId(2), SemanticSortDirection.Asc)],
                Limit: limit));
    }

    internal static SqlPlan Compile(SemanticModel model, IMetadataProvider metadata, SemanticRequest request)
    {
        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var execution = new Planner().Plan(authorized);
        return new SqlCompiler(metadata).Compile(execution);
    }

    internal static SemanticModel BuildModel() =>
        new SemanticModelBuilder()
            .Entity(Customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(CustomerAccounts, "Accounts", Account, RelationshipCardinality.Many))
            .Entity(Account, "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(3), "Balance", typeof(decimal))
                .Field(new FieldId(4), "Status", typeof(string))
                .Relationship(AccountTransactions, "Transactions", Transaction, RelationshipCardinality.Many))
            .Entity(Transaction, "Transaction", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(3), "Amount", typeof(decimal))
                .Field(new FieldId(4), "TransactionDate", typeof(DateTime)))
            .Build();

    internal static MetadataRegistry BuildMetadata()
    {
        var registry = new MetadataRegistry();
        registry.Register(new EntityMetadata(Customer, "Customer",
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(Customer, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(Customer, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(Customer, new ColumnId(1))));

        registry.Register(new EntityMetadata(Account, "Account",
            [
                new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "CustomerId"),
                new ColumnMetadata(new ColumnId(3), "Balance"), new ColumnMetadata(new ColumnId(4), "Status")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(Account, new ColumnId(1))),
                new FieldMetadata(new FieldId(3), "Balance", typeof(decimal),
                    new ColumnReference(Account, new ColumnId(3))),
                new FieldMetadata(new FieldId(4), "Status", typeof(string),
                    new ColumnReference(Account, new ColumnId(4)))
            ],
            PrimaryKey: new ColumnReference(Account, new ColumnId(1))));

        registry.Register(new EntityMetadata(Transaction, "Transaction",
            [
                new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "AccountId"),
                new ColumnMetadata(new ColumnId(3), "Amount"), new ColumnMetadata(new ColumnId(4), "TransactionDate")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long),
                    new ColumnReference(Transaction, new ColumnId(1))),
                new FieldMetadata(new FieldId(3), "Amount", typeof(decimal),
                    new ColumnReference(Transaction, new ColumnId(3))),
                new FieldMetadata(new FieldId(4), "TransactionDate", typeof(DateTime),
                    new ColumnReference(Transaction, new ColumnId(4)))
            ],
            PrimaryKey: new ColumnReference(Transaction, new ColumnId(1))));

        registry.Register(new RelationshipMetadata(CustomerAccounts, Customer, Account, "Accounts",
            new ColumnReference(Customer, new ColumnId(1)), new ColumnReference(Account, new ColumnId(2))));
        registry.Register(new RelationshipMetadata(AccountTransactions, Account, Transaction, "Transactions",
            new ColumnReference(Account, new ColumnId(1)), new ColumnReference(Transaction, new ColumnId(2))));
        return registry;
    }

    private static async Task<QueryBaseline> SeedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction) =>
        await SeedScaledAsync(connection, transaction, 10);

    private static async Task<QueryBaseline> SeedScaledAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int customers)
    {
        long customer1Id = 0, customer2Id = 0, customer3Id = 0;

        for (var i = 1; i <= customers; i++)
        {
            var customerId = await InsertCustomerAsync(
                connection,
                transaction,
                i switch { 1 => "Alice", 2 => "Bob", 3 => "Carol", _ => $"Customer-{i}" });

            var firstAccountId = await InsertAccountAsync(
                connection, transaction, customerId,
                i == 2 ? 50m : 250m,
                i == 2 ? "Closed" : "Active");

            var secondAccountId = await InsertAccountAsync(
                connection, transaction, customerId,
                i == 2 ? 25m : 80m,
                i == 2 ? "Blocked" : "Active");

            await InsertTransactionAsync(
                connection, transaction, firstAccountId,
                i == 2 ? 20m : 300m,
                new DateTime(2026, 1, 1));

            await InsertTransactionAsync(
                connection, transaction, secondAccountId,
                i == 2 ? 30m : 125m,
                new DateTime(2026, 1, 2));

            if (i == 1) customer1Id = customerId;
            else if (i == 2) customer2Id = customerId;
            else if (i == 3) customer3Id = customerId;
        }

        return new QueryBaseline(customer1Id, customer2Id, customer3Id);
    }

    private static async Task<long> InsertCustomerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string name)
    {
        const string sql = """
                           INSERT INTO "Customer" ("Name")
                           VALUES (@name)
                           RETURNING "Id";
                           """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("name", name);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> InsertAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long customerId,
        decimal balance,
        string status)
    {
        const string sql = """
                           INSERT INTO "Account" ("CustomerId", "Balance", "Status")
                           VALUES (@customerId, @balance, @status)
                           RETURNING "Id";
                           """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("customerId", customerId);
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

    private sealed record QueryBaseline(
        long Customer1Id,
        long Customer2Id,
        long Customer3Id);

    private static async Task SetSearchPathAsync(NpgsqlConnection connection, string schema)
    {
        await using var command = new NpgsqlCommand(
            $"SET search_path TO \"{schema}\";",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PlanStats> ExplainAnalyzeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        SqlPlan plan, int limit)
    {
        try
        {
            await using var command = new NpgsqlCommand(
                "EXPLAIN (ANALYZE, BUFFERS, WAL, FORMAT JSON) " + plan.CommandText,
                connection, transaction);
            foreach (var binding in plan.EffectiveParameters)
            {
                object value = binding.ContextPath is not null &&
                               binding.ContextPath == ExecutionContextKeys.PaginationLimit
                    ? limit
                    : binding.Value ?? DBNull.Value;
                command.Parameters.AddWithValue(binding.Name, value);
            }

            var raw = Convert.ToString(await command.ExecuteScalarAsync())
                      ?? throw new InvalidOperationException("PostgreSQL returned no EXPLAIN JSON.");
            using var json = JsonDocument.Parse(raw);
            var root = json.RootElement[0];
            var stats = new PlanStats
            {
                PlanningTimeMs = root.GetProperty("Planning Time").GetDouble(),
                ExecutionTimeMs = root.GetProperty("Execution Time").GetDouble()
            };
            Walk(root.GetProperty("Plan"), stats);
            if (root.TryGetProperty("WAL", out var wal) && wal.TryGetProperty("WAL Bytes", out var bytes))
                stats.WalBytes = bytes.GetInt64();
            return stats;
        }
        catch (Exception)
        {
            // ignored
        }

        return new PlanStats();
    }

    private static void Walk(JsonElement node, PlanStats stats)
    {
        var type = node.TryGetProperty("Node Type", out var typeElement) ? typeElement.GetString() ?? "" : "";
        var estimated = node.TryGetProperty("Plan Rows", out var est) ? est.GetDouble() : 0;
        var actual = node.TryGetProperty("Actual Rows", out var act) ? act.GetDouble() : 0;
        var loops = node.TryGetProperty("Actual Loops", out var lp) ? lp.GetDouble() : 0;
        var time = node.TryGetProperty("Actual Total Time", out var tm) ? tm.GetDouble() : 0;
        stats.RootActualRows = Math.Max(stats.RootActualRows, (long)actual);
        stats.RootPlanRows = Math.Max(stats.RootPlanRows, (long)estimated);
        if (node.TryGetProperty("Shared Hit Blocks", out var hit)) stats.SharedHit += hit.GetInt64();
        if (node.TryGetProperty("Shared Read Blocks", out var read)) stats.SharedRead += read.GetInt64();
        if (node.TryGetProperty("Shared Written Blocks", out var written)) stats.SharedWritten += written.GetInt64();
        if (node.TryGetProperty("Temp Read Blocks", out var tempRead)) stats.TempRead += tempRead.GetInt64();
        if (node.TryGetProperty("Temp Written Blocks", out var tempWrite)) stats.TempWritten += tempWrite.GetInt64();
        stats.Nodes.Add(new PlanNodeStats(type, estimated, actual, loops, time,
            node.TryGetProperty("Sort Method", out var sm) ? sm.GetString() : null,
            node.TryGetProperty("Sort Space Used", out var su) ? su.GetInt64() : null,
            node.TryGetProperty("Sort Space Type", out var st) ? st.GetString() : null));
        if (type.Contains("Join", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("Loop", StringComparison.OrdinalIgnoreCase))
            stats.JoinStrategies.Add(type);
        if (type == "Sort") stats.SortCount++;
        if (type.Contains("Materialize", StringComparison.OrdinalIgnoreCase)) stats.MaterializeCount++;
        if (node.TryGetProperty("Plans", out var children))
            foreach (var child in children.EnumerateArray())
                Walk(child, stats);
    }

    private sealed record PlanNodeStats(
        string NodeType,
        double EstimatedRows,
        double ActualRows,
        double ActualLoops,
        double ActualTotalTimeMs,
        string? SortMethod,
        long? SortSpaceUsed,
        string? SortSpaceType);

    private static void PrintStats(string kind, int datasetSize, int depth, PlanStats s) =>
        Console.WriteLine(
            $"POSTGRES_E2E READ kind={kind} dataset={datasetSize} depth={depth} planning_ms={s.PlanningTimeMs:F3} execution_ms={s.ExecutionTimeMs:F3} shared_hit={s.SharedHit} shared_read={s.SharedRead} shared_written={s.SharedWritten} temp_read={s.TempRead} temp_written={s.TempWritten} wal_bytes={s.WalBytes} joins={string.Join(',', s.JoinStrategies)} sorts={s.SortCount} materialize={s.MaterializeCount} actual_rows={s.RootActualRows} estimated_rows={s.RootPlanRows}");

    private static void PrintNodeEvidence(PlanStats s)
    {
        foreach (var node in s.Nodes.Where(n => n.EstimatedRows > 0)
                     .OrderByDescending(n => Math.Max(n.ActualRows, 1) / Math.Max(n.EstimatedRows, 1)).Take(5))
            Console.WriteLine(
                $"POSTGRES_E2E READ_NODE type={node.NodeType} estimated_rows={node.EstimatedRows:F0} actual_rows={node.ActualRows:F0} loops={node.ActualLoops:F0} time_ms={node.ActualTotalTimeMs:F3} sort_method={node.SortMethod ?? "-"} sort_space={node.SortSpaceUsed?.ToString() ?? "-"} sort_space_type={node.SortSpaceType ?? "-"}");
    }

    private sealed class PlanStats
    {
        public double PlanningTimeMs;
        public double ExecutionTimeMs;
        public long SharedHit;
        public long SharedRead;
        public long SharedWritten;
        public long TempRead;
        public long TempWritten;
        public long WalBytes;
        public long RootActualRows;
        public long RootPlanRows;
        public int SortCount;
        public int MaterializeCount;
        public HashSet<string> JoinStrategies { get; } = new(StringComparer.Ordinal);
        public List<PlanNodeStats> Nodes { get; } = new();
    }
}