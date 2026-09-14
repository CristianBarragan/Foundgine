using System.Text.Json;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Providers.Storage.Sql.Mutation;
using Foundgine.Providers.Storage.Sql.Mutation.Postgres;
using Npgsql;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

/// <summary>
/// PostgreSQL E2E physical proof: the flagship semantic graph is lowered all the
/// way to PostgreSQL 17. The test is intentionally integration-only and uses
/// FOUNDGINE_POSTGRES_CONNECTION_STRING so the repository remains runnable in
/// environments that do not have PostgreSQL installed.
///
/// Pipeline proved here:
/// Semantic graph -> semantic plan -> execution IR -> PostgreSQL batch compiler
/// -> one physical PostgreSQL statement -> Npgsql -> PostgreSQL 17 -> RETURNING
/// results -> transaction rollback.
///
/// EXPLAIN ANALYZE is executed in a separate rollback-only transaction, with
/// BUFFERS/WAL/JSON enabled, so this test also emits the first measurement gate
/// without changing the compiler.
/// </summary>
public sealed class PostgresE2ETests
{
    private const string ConnectionEnvironmentVariable = "FOUNDGINE_POSTGRES_CONNECTION_STRING";


    internal static MetadataRegistry BuildMetadata()
    {
        var registry = new MetadataRegistry();

        registry.Register(new EntityMetadata(
            ComplexSemanticMutationE2ETests.Customer,
            "Customer",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "Name"),
                new ColumnMetadata(new ColumnId(3), "Status"),
                new ColumnMetadata(new ColumnId(4), "Notes")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Customer, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Customer, new ColumnId(2))),
                new FieldMetadata(new FieldId(3), "Status", typeof(string),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Customer, new ColumnId(3))),
                new FieldMetadata(new FieldId(8), "Notes", typeof(string),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Customer, new ColumnId(4)))
            ],
            PrimaryKey: new ColumnReference(ComplexSemanticMutationE2ETests.Customer, new ColumnId(1))));

        registry.Register(new EntityMetadata(
            ComplexSemanticMutationE2ETests.Profile,
            "Profile",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "CustomerId"),
                new ColumnMetadata(new ColumnId(3), "Name")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Profile, new ColumnId(1))),
                new FieldMetadata(new FieldId(4), "CustomerId", typeof(long),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Profile, new ColumnId(2))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Profile, new ColumnId(3)))
            ],
            PrimaryKey: new ColumnReference(ComplexSemanticMutationE2ETests.Profile, new ColumnId(1))));

        registry.Register(new EntityMetadata(
            ComplexSemanticMutationE2ETests.Account,
            "Account",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "CustomerId"),
                new ColumnMetadata(new ColumnId(3), "Name"),
                new ColumnMetadata(new ColumnId(4), "Status")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Account, new ColumnId(1))),
                new FieldMetadata(new FieldId(4), "CustomerId", typeof(long),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Account, new ColumnId(2))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Account, new ColumnId(3))),
                new FieldMetadata(new FieldId(3), "Status", typeof(string),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Account, new ColumnId(4)))
            ],
            PrimaryKey: new ColumnReference(ComplexSemanticMutationE2ETests.Account, new ColumnId(1))));

        registry.Register(new EntityMetadata(
            ComplexSemanticMutationE2ETests.Order,
            "Order",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "AccountId"),
                new ColumnMetadata(new ColumnId(3), "Status")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Order, new ColumnId(1))),
                new FieldMetadata(new FieldId(5), "AccountId", typeof(long),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Order, new ColumnId(2))),
                new FieldMetadata(new FieldId(3), "Status", typeof(string),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Order, new ColumnId(3)))
            ],
            PrimaryKey: new ColumnReference(ComplexSemanticMutationE2ETests.Order, new ColumnId(1))));

        registry.Register(new EntityMetadata(
            ComplexSemanticMutationE2ETests.Payment,
            "Payment",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "OrderId"),
                new ColumnMetadata(new ColumnId(3), "Amount"),
                new ColumnMetadata(new ColumnId(4), "Status")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Payment, new ColumnId(1))),
                new FieldMetadata(new FieldId(6), "OrderId", typeof(long),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Payment, new ColumnId(2))),
                new FieldMetadata(new FieldId(7), "Amount", typeof(decimal),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Payment, new ColumnId(3))),
                new FieldMetadata(new FieldId(3), "Status", typeof(string),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Payment, new ColumnId(4)))
            ],
            PrimaryKey: new ColumnReference(ComplexSemanticMutationE2ETests.Payment, new ColumnId(1))));

        registry.Register(new EntityMetadata(
            ComplexSemanticMutationE2ETests.Audit,
            "Audit",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "CustomerId"),
                new ColumnMetadata(new ColumnId(3), "Kind")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Audit, new ColumnId(1))),
                new FieldMetadata(new FieldId(4), "CustomerId", typeof(long),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Audit, new ColumnId(2))),
                new FieldMetadata(new FieldId(9), "Kind", typeof(string),
                    new ColumnReference(ComplexSemanticMutationE2ETests.Audit, new ColumnId(3)))
            ],
            PrimaryKey: new ColumnReference(ComplexSemanticMutationE2ETests.Audit, new ColumnId(1))));

        registry.Register(new RelationshipMetadata(
            ComplexSemanticMutationE2ETests.CustomerProfiles,
            ComplexSemanticMutationE2ETests.Customer,
            ComplexSemanticMutationE2ETests.Profile,
            "Profiles",
            new ColumnReference(ComplexSemanticMutationE2ETests.Customer, new ColumnId(1)),
            new ColumnReference(ComplexSemanticMutationE2ETests.Profile, new ColumnId(2))));

        registry.Register(new RelationshipMetadata(
            ComplexSemanticMutationE2ETests.CustomerAccounts,
            ComplexSemanticMutationE2ETests.Customer,
            ComplexSemanticMutationE2ETests.Account,
            "Accounts",
            new ColumnReference(ComplexSemanticMutationE2ETests.Customer, new ColumnId(1)),
            new ColumnReference(ComplexSemanticMutationE2ETests.Account, new ColumnId(2))));

        registry.Register(new RelationshipMetadata(
            ComplexSemanticMutationE2ETests.AccountOrders,
            ComplexSemanticMutationE2ETests.Account,
            ComplexSemanticMutationE2ETests.Order,
            "Orders",
            new ColumnReference(ComplexSemanticMutationE2ETests.Account, new ColumnId(1)),
            new ColumnReference(ComplexSemanticMutationE2ETests.Order, new ColumnId(2))));

        registry.Register(new RelationshipMetadata(
            ComplexSemanticMutationE2ETests.OrderPayments,
            ComplexSemanticMutationE2ETests.Order,
            ComplexSemanticMutationE2ETests.Payment,
            "Payments",
            new ColumnReference(ComplexSemanticMutationE2ETests.Order, new ColumnId(1)),
            new ColumnReference(ComplexSemanticMutationE2ETests.Payment, new ColumnId(2))));

        registry.Register(new RelationshipMetadata(
            ComplexSemanticMutationE2ETests.CustomerAudits,
            ComplexSemanticMutationE2ETests.Customer,
            ComplexSemanticMutationE2ETests.Audit,
            "Audits",
            new ColumnReference(ComplexSemanticMutationE2ETests.Customer, new ColumnId(1)),
            new ColumnReference(ComplexSemanticMutationE2ETests.Audit, new ColumnId(2))));

        registry.Register(new RelationshipMetadata(
            ComplexSemanticMutationE2ETests.AuditCustomer,
            ComplexSemanticMutationE2ETests.Audit,
            ComplexSemanticMutationE2ETests.Customer,
            "Customer",
            new ColumnReference(ComplexSemanticMutationE2ETests.Audit, new ColumnId(2)),
            new ColumnReference(ComplexSemanticMutationE2ETests.Customer, new ColumnId(1))));

        return registry;
    }


    [PostgreSqlFact]
    public async Task PostgresE2E_runs_complete_semantic_to_postgresql17_pipeline_and_rolls_back()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await SetSearchPathAsync(connection, "fg_mutation");

        var metadata = BuildMetadata();
        var semanticPlan = new SemanticMutationPlanner().Plan(
            ComplexSemanticMutationE2ETests.BuildGraph());

        var executionIr = new SemanticMutationExecutionLowerer(metadata).Lower(semanticPlan);
        executionIr.ValidateDerivedDependencies();

        var compiled = new PostgresBatchedMutationCompiler(metadata).Compile(executionIr);
        Assert.NotEmpty(compiled.CommandText);
        Assert.Contains("MERGE INTO", compiled.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__fg_corr", compiled.CommandText, StringComparison.Ordinal);
        Assert.Contains("unnest", compiled.CommandText, StringComparison.OrdinalIgnoreCase);

        var before = await SnapshotAsync(connection);
        var explain = await ExplainAnalyzeAsync(connection, compiled);

        Assert.True(explain.ExecutionTimeMs >= 0);
        Assert.True(explain.PlanningTimeMs >= 0);
        Assert.True(explain.RootActualRows >= 0);
        Assert.NotEmpty(explain.JoinStrategies);

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var provider = new PostgresBatchedMutationExecutionProvider(connection, metadata, transaction);
            var result = provider.ExecuteBatch(executionIr, new ExecutionContext());

            Assert.Equal(10, result.Results.Count);
            Assert.All(new[] { 0, 1, 2, 4, 6 }, i =>
            {
                Assert.True(result.Results[i].AffectedRows >= 1, $"operation {i} did not affect a row");
                Assert.NotNull(result.Results[i].ReturnedFields);
            });

            Assert.NotNull(result.Results[0].ReturnedFields);
            Assert.NotNull(result.Results[2].ReturnedFields);
            Assert.NotNull(result.Results[4].ReturnedFields);
            Assert.NotNull(result.Results[6].ReturnedFields);

            // Prove generated values really crossed PostgreSQL, rather than being
            // reconstructed by the client. Each downstream FK must match the
            // generated key returned by the preceding mutation.
            var customerId = Convert.ToInt64(result.Results[0].ReturnedFields![new FieldId(1)]);
            var accountId = Convert.ToInt64(result.Results[2].ReturnedFields![new FieldId(1)]);
            var orderId = Convert.ToInt64(result.Results[4].ReturnedFields![new FieldId(1)]);
            var paymentOrderId = Convert.ToInt64(result.Results[6].ReturnedFields![new FieldId(2)]);

            Assert.True(customerId > 0);
            Assert.True(accountId > 0);
            Assert.True(orderId > 0);
            Assert.Equal(orderId, paymentOrderId);

            var inside = await SnapshotAsync(connection, transaction);
            Assert.True(inside.CustomerCount >= before.CustomerCount + 1);
            Assert.True(inside.ProfileCount >= before.ProfileCount + 1);
            Assert.True(inside.AccountCount >= before.AccountCount + 1);
            Assert.True(inside.OrderCount >= before.OrderCount + 1);
            Assert.True(inside.PaymentCount >= before.PaymentCount + 1);
        }
        finally
        {
            await transaction.RollbackAsync();
        }

        var after = await SnapshotAsync(connection);
        Assert.Equal(before, after);
    }

    [PostgreSqlTheory]
    [InlineData(1, 1)]
    [InlineData(10, 1)]
    [InlineData(50, 2)]
    [InlineData(500, 3)]
    public async Task PostgresE2E_explain_matrix_is_available_for_batch_and_depth_measurement_gate(
        int batchSize,
        int depth)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var metadata = BuildMetadata();
        var graph = BuildMeasurementGraph(batchSize, depth);
        var semanticPlan = new SemanticMutationPlanner().Plan(graph);
        var executionIr = new SemanticMutationExecutionLowerer(metadata).Lower(semanticPlan);
        executionIr.ValidateDerivedDependencies();

        var compiled = new PostgresBatchedMutationCompiler(metadata).Compile(executionIr);
        var explain = await ExplainAnalyzeAsync(connection, compiled);

        Console.WriteLine($"POSTGRES_E2E batch={batchSize} depth={depth} " +
                          $"planning_ms={explain.PlanningTimeMs:F3} " +
                          $"execution_ms={explain.ExecutionTimeMs:F3} " +
                          $"shared_hit={explain.SharedHit} shared_read={explain.SharedRead} " +
                          $"shared_written={explain.SharedWritten} temp_read={explain.TempRead} " +
                          $"temp_written={explain.TempWritten} wal_bytes={explain.WalBytes} " +
                          $"joins={string.Join(',', explain.JoinStrategies)} " +
                          $"sorts={explain.SortCount} materialize={explain.MaterializeCount} " +
                          $"actual_rows={explain.RootActualRows} estimated_rows={explain.RootPlanRows}");
        foreach (var node in explain.Nodes.Where(n => n.EstimatedRows > 0 && n.ActualRows >= 0)
                     .OrderByDescending(n => Math.Max(n.ActualRows, 1) / Math.Max(n.EstimatedRows, 1))
                     .Take(5))
            Console.WriteLine(
                $"POSTGRES_E2E NODE type={node.NodeType} estimated_rows={node.EstimatedRows:F0} actual_rows={node.ActualRows:F0} loops={node.ActualLoops:F0} time_ms={node.ActualTotalTimeMs:F3} sort_method={node.SortMethod ?? "-"} sort_space={node.SortSpaceUsed?.ToString() ?? "-"} sort_space_type={node.SortSpaceType ?? "-"}");

        Assert.True(explain.ExecutionTimeMs >= 0);
        Assert.True(explain.PlanningTimeMs >= 0);
    }

    private sealed record DbSnapshot(
        long CustomerCount,
        long ProfileCount,
        long AccountCount,
        long OrderCount,
        long PaymentCount,
        long AuditCount);

    private static async Task<DbSnapshot> SnapshotAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction = null)
    {
        const string sql = """
                           SELECT
                               (SELECT COUNT(*) FROM "Customer"),
                               (SELECT COUNT(*) FROM "Profile"),
                               (SELECT COUNT(*) FROM "Account"),
                               (SELECT COUNT(*) FROM "Order"),
                               (SELECT COUNT(*) FROM "Payment"),
                               (SELECT COUNT(*) FROM "Audit");
                           """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("PostgreSQL returned no snapshot row.");
        return new DbSnapshot(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));
    }

    private static SemanticMutationOperationGraph BuildMeasurementGraph(int batchSize, int depth)
    {
        if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (depth < 1 || depth > 3) throw new ArgumentOutOfRangeException(nameof(depth));

        var baseOperations = ComplexSemanticMutationE2ETests.BuildGraph().Operations;
        // Use the generated-value chain 0 -> 2 -> 4 -> 6 as the depth-sensitive core.
        // The measurement matrix intentionally keeps the graph shape stable while
        // increasing independent create operations to exercise batch cardinality.
        var selected = new List<SemanticMutationOperation> { baseOperations[0] };
        if (depth >= 2)
            selected.Add(baseOperations[2] with
            {
                Fields = baseOperations[2].Fields.Select(f => f with
                {
                    Source = f.Source is null ? null : new SemanticMutationValueReference(0, f.Source.SourceField)
                }).ToArray(),
                Dependencies =
                [
                    new SemanticMutationDependency(0, 1, ComplexSemanticMutationE2ETests.Id,
                        ComplexSemanticMutationE2ETests.CustomerId, ComplexSemanticMutationE2ETests.CustomerAccounts)
                ]
            });
        if (depth >= 3)
            selected.Add(baseOperations[4] with
            {
                Fields = baseOperations[4].Fields.Select(f => f with
                {
                    Source = f.Source is null ? null : new SemanticMutationValueReference(1, f.Source.SourceField)
                }).ToArray(),
                Dependencies = []
            });

        var operations = new List<SemanticMutationOperation>(batchSize);
        operations.AddRange(selected);
        var suffix = 0;
        while (operations.Count < batchSize)
        {
            var op = baseOperations[0] with
            {
                Fields = baseOperations[0].Fields.Select(f =>
                    f.Field == ComplexSemanticMutationE2ETests.Name
                        ? f with { Value = $"Batch-{++suffix}" }
                        : f).ToArray(),
                Effects = baseOperations[0].Effects
            };
            operations.Add(op);
        }

        return new SemanticMutationOperationGraph(operations);
    }

    private static async Task SetSearchPathAsync(NpgsqlConnection connection, string schema)
    {
        await using var command = new NpgsqlCommand(
            $"SET search_path TO \"{schema}\";",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PlanStats> ExplainAnalyzeAsync(
        NpgsqlConnection connection,
        SqlBatchedMutationPlan plan)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using var command = new NpgsqlCommand(
                "EXPLAIN (ANALYZE, BUFFERS, WAL, FORMAT JSON) " + plan.CommandText,
                connection,
                transaction);

            foreach (var binding in plan.Parameters)
                command.Parameters.AddWithValue(binding.Name, binding.Value ?? DBNull.Value);

            var raw = Convert.ToString(await command.ExecuteScalarAsync())
                      ?? throw new InvalidOperationException("PostgreSQL returned no EXPLAIN JSON.");
            var json = JsonDocument.Parse(raw);
            var root = json.RootElement[0].GetProperty("Plan");
            var stats = new PlanStats();
            WalkPlan(root, stats);
            stats.PlanningTimeMs = json.RootElement[0].GetProperty("Planning Time").GetDouble();
            stats.ExecutionTimeMs = json.RootElement[0].GetProperty("Execution Time").GetDouble();

            if (json.RootElement[0].TryGetProperty("WAL", out var wal))
            {
                if (wal.TryGetProperty("WAL Bytes", out var bytes)) stats.WalBytes = bytes.GetInt64();
                if (wal.TryGetProperty("WAL Records", out var records)) stats.WalRecords = records.GetInt64();
                if (wal.TryGetProperty("WAL FPI", out var fpi)) stats.WalFpi = fpi.GetInt64();
            }

            return stats;
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static void WalkPlan(JsonElement node, PlanStats stats)
    {
        var type = node.TryGetProperty("Node Type", out var typeElement) ? typeElement.GetString() ?? "" : "";
        var planRows = node.TryGetProperty("Plan Rows", out var estimated) ? estimated.GetDouble() : 0;
        var actualRows = node.TryGetProperty("Actual Rows", out var actual) ? actual.GetDouble() : 0;
        var loops = node.TryGetProperty("Actual Loops", out var loopElement) ? loopElement.GetDouble() : 0;
        var totalTime = node.TryGetProperty("Actual Total Time", out var timeElement) ? timeElement.GetDouble() : 0;

        stats.RootActualRows = Math.Max(stats.RootActualRows, (long)actualRows);
        stats.RootPlanRows = Math.Max(stats.RootPlanRows, (long)planRows);
        if (node.TryGetProperty("Shared Hit Blocks", out var hit)) stats.SharedHit += hit.GetInt64();
        if (node.TryGetProperty("Shared Read Blocks", out var read)) stats.SharedRead += read.GetInt64();
        if (node.TryGetProperty("Shared Written Blocks", out var written)) stats.SharedWritten += written.GetInt64();
        if (node.TryGetProperty("Temp Read Blocks", out var tempRead)) stats.TempRead += tempRead.GetInt64();
        if (node.TryGetProperty("Temp Written Blocks", out var tempWrite)) stats.TempWritten += tempWrite.GetInt64();

        stats.Nodes.Add(new PlanNodeStats(type, planRows, actualRows, loops, totalTime,
            node.TryGetProperty("Sort Method", out var sortMethod) ? sortMethod.GetString() : null,
            node.TryGetProperty("Sort Space Used", out var sortSpace) ? sortSpace.GetInt64() : null,
            node.TryGetProperty("Sort Space Type", out var sortSpaceType) ? sortSpaceType.GetString() : null));

        if (type.Contains("Join", StringComparison.OrdinalIgnoreCase)) stats.JoinStrategies.Add(type);
        if (type == "Sort") stats.SortCount++;
        if (type.Contains("Materialize", StringComparison.OrdinalIgnoreCase)) stats.MaterializeCount++;
        if (node.TryGetProperty("Plans", out var children))
            foreach (var child in children.EnumerateArray())
                WalkPlan(child, stats);
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
        public long WalRecords;
        public long WalFpi;
        public long RootActualRows;
        public long RootPlanRows;
        public int SortCount;
        public int MaterializeCount;
        public HashSet<string> JoinStrategies { get; } = new(StringComparer.Ordinal);
        public List<PlanNodeStats> Nodes { get; } = new();
    }

    private sealed record DatabaseSnapshot(
        long CustomerCount,
        long ProfileCount,
        long AccountCount,
        long OrderCount,
        long PaymentCount,
        long AuditCount);
}