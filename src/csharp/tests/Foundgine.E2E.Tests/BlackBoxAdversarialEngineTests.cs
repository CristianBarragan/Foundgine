using Foundgine.Core.Semantic.Security;
using Foundgine.Runtime;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Serialization;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Providers.Storage.Sql;
using Foundgine.E2E.Tests.Banking;
using Foundgine.Core.Execution.Security;
using Microsoft.Data.Sqlite;
using Npgsql;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

/// <summary>
/// black-box adversarial gate. These tests deliberately exercise the
/// agent-facing JSON boundary through the real Foundgine engine, plan cache,
/// SQL compiler and execution provider rather than testing individual layers
/// in isolation.
/// </summary>
public sealed class BlackBoxAdversarialEngineTests
{
    [Fact]
    public void Hostile_structured_intent_is_rejected_before_semantic_execution()
    {
        const string json = """
                            {
                            "rootEntity": "Customer",
                            "selections": [{ "field": "Id" }],
                            "tenantId": 7,
                            "userId": "attacker",
                            "provider": "postgres",
                            "sql": "DROP TABLE Customer",
                            "authorization": true
                            }
                            """;

        var adapter = new JsonReadIntentAdapter();

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));

        Assert.Contains("tenantId", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hostile_filter_value_remains_a_parameter_and_cannot_change_authorization_scope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedCustomersAsync(connection);

        var compiler = new CountingSqlCompiler(BankingRelationalMetadata.Build());
        var engine = CreateEngine(
            new SqlExecutionProvider(connection),
            BankingRelationalMetadata.Build(),
            new TenantPolicy(),
            compiler);

        const string json = """
                            {
                            "rootEntity": "Customer",
                            "selections": [
                            { "field": "Id" },
                            { "field": "Name" }
                            ],
                            "filter": {
                            "kind": "field",
                            "field": "Name",
                            "operator": "Eq",
                            "value": "' OR 1=1 --"
                            }
                            }
                            """;

        var intent = new JsonReadIntentAdapter().Parse(json);
        var result = await engine.ExecuteAsync(
            intent,
            new ExecutionContext(new Dictionary<string, object?> { ["user.TenantId"] = 7 }));

        Assert.Empty(result.Rows);
        Assert.NotNull(result.Evidence);
        Assert.NotNull(result.Receipt);
        Assert.NotNull(compiler.LastPlan);
        Assert.DoesNotContain("' OR 1=1 --", compiler.LastPlan!.CommandText, StringComparison.Ordinal);
        Assert.Contains("' OR 1=1 --", compiler.LastPlan.EffectiveParameters.Select(x => x.Value?.ToString()));
    }

    [Fact]
    public async Task Runtime_context_changes_results_without_recompiling_or_reusing_authorization_values()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedCustomersAsync(connection);

        var compiler = new CountingSqlCompiler(BankingRelationalMetadata.Build());
        var engine = CreateEngine(
            new SqlExecutionProvider(connection),
            BankingRelationalMetadata.Build(),
            new TenantPolicy(),
            compiler);

        var intent = new JsonReadIntentAdapter().Parse("""
                                                       {
                                                       "rootEntity": "Customer",
                                                       "selections": [
                                                       { "field": "Id" },
                                                       { "field": "Name" }
                                                       ]
                                                       }
                                                       """);

        var tenantSeven = await engine.ExecuteAsync(
            intent,
            new ExecutionContext(new Dictionary<string, object?> { ["user.TenantId"] = 7 }));

        var tenantEight = await engine.ExecuteAsync(
            intent,
            new ExecutionContext(new Dictionary<string, object?> { ["user.TenantId"] = 8 }));

        Assert.Single(tenantSeven.Rows);
        Assert.Equal(1L, tenantSeven.Rows[0].Values["__fg_0_Id"]);
        Assert.Single(tenantEight.Rows);
        Assert.Equal(2L, tenantEight.Rows[0].Values["__fg_0_Id"]);
        Assert.Equal(1, compiler.Count);
    }

    [Fact]
    public async Task Authorization_denial_prevents_provider_compilation_and_execution()
    {
        var compiler = new CountingCompiler();
        var provider = new CountingProvider();
        var engine = CreateEngine(
            provider,
            BankingRelationalMetadata.Build(),
            new DenyCustomerPolicy(),
            compiler);

        var intent = new JsonReadIntentAdapter().Parse("""
                                                       {
                                                       "rootEntity": "Customer",
                                                       "selections": [{ "field": "Id" }]
                                                       }
                                                       """);

        await Assert.ThrowsAsync<SemanticAuthorizationException>(() =>
            engine.ExecuteAsync(
                intent,
                new ExecutionContext(new Dictionary<string, object?> { ["user.TenantId"] = 7 })));

        Assert.Equal(0, compiler.Count);
        Assert.Equal(0, provider.Count);
    }

    [Fact]
    public Task Adversarial_corpus_fails_closed_at_the_json_boundary()
    {
        var adapter = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions
        {
            MaxSelectionDepth = 2,
            MaxSelections = 3,
            MaxFilterDepth = 2,
            MaxFilterNodes = 3,
            MaxJsonValueDepth = 2,
            RejectUnknownProperties = true
        });

        var corpus = new[]
        {
            """{"rootEntity":"Customer","selections":[{"field":"Id","children":[{"field":"Name","children":[{"field":"TenantId"}]}]}]}""",
            """{"rootEntity":"Customer","selections":[{"field":"Id"},{"field":"Name"},{"field":"TenantId"},{"field":"Id"}]}""",
            """{"rootEntity":"Customer","selections":[{"field":"Id"}],"filter":{"kind":"and","expressions":[{"kind":"field","field":"Name","operator":"Eq","value":"a"},{"kind":"field","field":"Name","operator":"Eq","value":"b"},{"kind":"field","field":"Name","operator":"Eq","value":"c"},{"kind":"field","field":"Name","operator":"Eq","value":"d"}]}}""",
            """{"rootEntity":"Customer","selections":[{"field":"Id"}],"filter":{"kind":"relationship","relationship":"Accounts","quantifier":"Some","predicate":{"kind":"relationship","relationship":"Customer","quantifier":"Some","predicate":{"kind":"field","field":"Name","operator":"Eq","value":"x"}}}}""",
            """{"rootEntity":"Customer","selections":[{"field":"Id"}],"options":{"tenantId":7}}"""
        };

        foreach (var json in corpus)
            Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));
        return Task.CompletedTask;
    }

    private static FoundgineEngine CreateEngine(
        IExecutionProvider provider,
        Foundgine.Core.Semantic.Metadata.IMetadataProvider metadata,
        ISemanticAuthorizationPolicy policy,
        IProviderPlanCompiler? compiler = null) =>
        new(
            new FoundgineOptions
            {
                Model = BankingSemanticModel.Build(),
                AuthorizationPolicy = policy
            },
            compiler ?? new SqlCompiler(metadata),
            provider);

    private static async Task SeedCustomersAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE "Customer" (
                              "Id" INTEGER PRIMARY KEY,
                              "Name" TEXT NOT NULL,
                              "TenantId" INTEGER NOT NULL
                              );
                              INSERT INTO "Customer" ("Id", "Name", "TenantId")
                              VALUES (1, 'Alice', 7), (2, 'Bob', 8);
                              """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TenantPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override AuthorizationPredicate? GetPredicate(
            EntityId entityId,
            AuthorizationOperation operation) =>
            operation == AuthorizationOperation.Read && entityId == BankingSemanticModel.Customer
                ? AuthorizationPredicate.Equal(
                    AuthorizationPredicate.Member(
                        AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
                    AuthorizationPredicate.Member(
                        AuthorizationPredicate.ContextParameter("user"), "TenantId"))
                : null;
    }

    private sealed class DenyCustomerPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessEntity(EntityId entityId) =>
            entityId != BankingSemanticModel.Customer;
    }

    private sealed class CountingCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
        IProviderSecurityConformanceEvaluator
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants =>
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id).ToArray();

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
            return new TestPlan();
        }
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
        public SqlPlan? LastPlan { get; private set; }

        public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan) =>
            new(
                plan.Provider,
                ir.RequiredSecurityInvariants,
                ir.RequiredSecurityInvariants.Where(PreservedSecurityInvariants.Contains).ToArray(),
                Array.Empty<string>());


        public ProviderPlan Compile(ExecutionIR ir)
        {
            Count++;
            LastPlan = _inner.Compile(ir);
            return LastPlan;
        }
    }

    private sealed class CountingProvider : IExecutionProvider
    {
        public int Count { get; private set; }

        public Task<ExecutionResult> ExecuteAsync(
            ProviderPlan plan,
            ExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.FromResult(new ExecutionResult(Array.Empty<ExecutionRow>()));
        }
    }

    private sealed record TestPlan() : ProviderPlan("test");
}