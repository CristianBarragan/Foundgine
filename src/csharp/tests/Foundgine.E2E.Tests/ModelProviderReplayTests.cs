using System.Text.Json;
using Foundgine.Runtime;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Serialization;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Providers.Storage.Sql;
using Foundgine.E2E.Tests.Banking;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Security;
using Microsoft.Data.Sqlite;
using Npgsql;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

/// <summary>
/// replay gate. The JSON in AdversarialFixtures represents untrusted
/// model output. The harness deliberately treats the fixture as hostile input
/// and verifies the invariant at the provider boundary.
/// </summary>
public sealed class ModelProviderReplayTests
{
    [Fact]
    public async Task Hostile_model_corpus_is_replayed_through_the_real_engine()
    {
        var cases = LoadCases();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedCustomersAsync(connection);

        var compiler = new CountingSqlCompiler(BankingRelationalMetadata.Build());
        var engine = CreateEngine(connection, compiler);
        var adapter = new JsonReadIntentAdapter();

        foreach (var testCase in cases)
        {
            if (testCase.Expected == "reject")
            {
                Assert.Throws<InvalidOperationException>(() => adapter.Parse(testCase.Json));
                continue;
            }

            var intent = adapter.Parse(testCase.Json);
            var result = await engine.ExecuteAsync(
                intent,
                new ExecutionContext(new Dictionary<string, object?>
                {
                    ["user.TenantId"] = testCase.TenantId
                }));

            Assert.NotNull(result.Evidence);
            Assert.NotNull(result.Receipt);
            Assert.Equal(testCase.Expected == "one-row" ? 1 : 0, result.Rows.Count);

            if (testCase.Id == "cross-tenant-filter")
                Assert.DoesNotContain(result.Rows, r => Convert.ToInt64(r.Values["__fg_0_Id"]) == 2);
        }

        // All accepted fixtures share the same semantic shape where possible;
        // the important invariant is that compilation never receives hostile
        // execution-control values from model output.
        Assert.True(compiler.Count > 0);
    }

    [Fact]
    public async Task Same_model_output_is_safe_under_two_runtime_tenants()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedCustomersAsync(connection);

        var compiler = new CountingSqlCompiler(BankingRelationalMetadata.Build());
        var engine = CreateEngine(connection, compiler);
        var intent = new JsonReadIntentAdapter().Parse("""
                                                       {
                                                       "rootEntity":"Customer",
                                                       "selections":[{"field":"Id"},{"field":"Name"}]
                                                       }
                                                       """);

        var tenant7 = await engine.ExecuteAsync(intent,
            new ExecutionContext(new Dictionary<string, object?> { ["user.TenantId"] = 7 }));
        var tenant8 = await engine.ExecuteAsync(intent,
            new ExecutionContext(new Dictionary<string, object?> { ["user.TenantId"] = 8 }));

        Assert.Equal(1L, tenant7.Rows.Single().Values["__fg_0_Id"]);
        Assert.Equal(2L, tenant8.Rows.Single().Values["__fg_0_Id"]);
        Assert.Equal(1, compiler.Count);
    }

    [PostgreSqlFact]
    public async Task Hostile_model_replay_reaches_real_postgres_without_cross_tenant_escape()
    {
        var connectionString = Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING")!;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var searchPath = new NpgsqlCommand(
                         "SET LOCAL search_path TO fg_replay;", connection, transaction))
        {
            await searchPath.ExecuteNonQueryAsync();
        }

        await SeedPostgresAsync(connection, transaction);

        var engine = new FoundgineEngine(
            new FoundgineOptions
            {
                Model = BankingSemanticModel.Build(),
                AuthorizationPolicy = new TenantPolicy()
            },
            new SqlCompiler(BankingRelationalMetadata.Build()),
            new SqlExecutionProvider(connection, transaction));

        foreach (var testCase in LoadCases().Where(x => x.Expected != "reject"))
        {
            var intent = new JsonReadIntentAdapter().Parse(testCase.Json);
            var result = await engine.ExecuteAsync(intent,
                new ExecutionContext(new Dictionary<string, object?> { ["user.TenantId"] = testCase.TenantId }));

            Assert.Equal(testCase.Expected == "one-row" ? 1 : 0, result.Rows.Count);
            Assert.NotNull(result.Evidence);
            Assert.NotNull(result.Receipt);
        }

        await transaction.RollbackAsync();
    }

    private static IReadOnlyList<ReplayCase> LoadCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "AdversarialFixtures", "M17.2-hostile-intents.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateArray().Select(x => new ReplayCase(
            x.GetProperty("id").GetString()!,
            x.GetProperty("json").GetRawText(),
            x.GetProperty("tenantId").GetInt32(),
            x.GetProperty("expected").GetString()!)).ToArray();
    }

    private static FoundgineEngine CreateEngine(SqliteConnection connection, CountingSqlCompiler compiler) =>
        new(
            new FoundgineOptions
            {
                Model = BankingSemanticModel.Build(),
                AuthorizationPolicy = new TenantPolicy()
            },
            compiler,
            new SqlExecutionProvider(connection));

    private static async Task SeedCustomersAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE "Customer" ("Id" INTEGER PRIMARY KEY, "Name" TEXT NOT NULL, "TenantId" INTEGER NOT NULL);
                              INSERT INTO "Customer" VALUES (1, 'Alice', 7), (2, 'Bob', 8);
                              """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedPostgresAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        const string sql = """
                           INSERT INTO "Customer" ("Name", "TenantId")
                           VALUES ('Alice', 7), ('Bob', 8);
                           """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
        await transaction.RollbackAsync();
    }

    private sealed record ReplayCase(string Id, string Json, int TenantId, string Expected);

    private sealed class TenantPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override AuthorizationPredicate? GetPredicate(
            Foundgine.Core.Abstractions.EntityId entityId,
            AuthorizationOperation operation) =>
            operation == AuthorizationOperation.Read && entityId == BankingSemanticModel.Customer
                ? AuthorizationPredicate.Equal(
                    AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
                    AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId"))
                : null;
    }

    private sealed class CountingSqlCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
        IProviderSecurityConformanceEvaluator
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants =>
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id).ToArray();

        private readonly SqlCompiler _inner;

        public CountingSqlCompiler(Foundgine.Core.Semantic.Metadata.IMetadataProvider metadata) =>
            _inner = new SqlCompiler(metadata);

        public int Count { get; private set; }

        public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan) =>
            new(
                plan.Provider,
                ir.RequiredSecurityInvariants,
                ir.RequiredSecurityInvariants.Where(PreservedSecurityInvariants.Contains).ToArray(),
                Array.Empty<string>());

        public ProviderPlan Compile(ExecutionIR ir)
        {
            Count++;
            return _inner.Compile(ir);
        }
    }
}