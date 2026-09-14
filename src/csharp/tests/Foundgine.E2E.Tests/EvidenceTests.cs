using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using FoundgineExecutionContext = Foundgine.Core.Execution.ExecutionContext;
using Foundgine.Generated;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Security;
using Foundgine.Providers.Storage.Sql;
using Microsoft.Data.Sqlite;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

public sealed class EvidenceTests
{
    [Fact]
    public async Task Sql_execution_returns_provider_neutral_evidence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "CREATE TABLE contracts (id INTEGER NOT NULL, contract_type INTEGER NOT NULL, tenant_id INTEGER NOT NULL);";
            await command.ExecuteNonQueryAsync();
            command.CommandText = "INSERT INTO contracts (id, contract_type, tenant_id) VALUES (1, 0, 7), (2, 1, 9);";
            await command.ExecuteNonQueryAsync();
        }

        var authorization = GeneratedMetadata.Registry.Authorizations.Single();
        var semanticGraph = new SemanticGraph();
        semanticGraph.AddRoot(
            new EntityId(3),
            new[] { new FieldId(1), new FieldId(3) },
            authorization.Predicate);

        var logicalPlan = new Planner().Plan(semanticGraph) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var sqlPlan = new SqlCompiler(GeneratedMetadata.Registry).Compile(logicalPlan);

        var result = await new SqlExecutionProvider(connection).ExecuteAsync(
            sqlPlan,
            new FoundgineExecutionContext(new Dictionary<string, object?>
            {
                ["user.TenantId"] = 7
            }));

        Assert.NotNull(result.Evidence);
        var evidence = result.Evidence!;
        Assert.Equal("sql", evidence.Provider);
        Assert.False(string.IsNullOrWhiteSpace(evidence.PlanFingerprint));
        Assert.Contains(logicalPlan.Root.Id, evidence.AuthorizedNodeIds);
        Assert.Equal(1, evidence.RowsReturned);
        Assert.True(evidence.ElapsedMilliseconds >= 0);
        Assert.False(string.IsNullOrWhiteSpace(evidence.ProviderOperationFingerprint));
        Assert.NotEqual(sqlPlan.CommandText, evidence.ProviderOperationFingerprint);
    }

    [Fact]
    public async Task Equivalent_sql_plans_produce_the_same_plan_fingerprint()
    {
        var authorization = GeneratedMetadata.Registry.Authorizations.Single();

        var graph1 = new SemanticGraph();
        graph1.AddRoot(new EntityId(3), new[] { new FieldId(1), new FieldId(3) }, authorization.Predicate);
        var graph2 = new SemanticGraph();
        graph2.AddRoot(new EntityId(3), new[] { new FieldId(1), new FieldId(3) }, authorization.Predicate);

        var compiler = new SqlCompiler(GeneratedMetadata.Registry);
        var sql1 = compiler.Compile(new Planner().Plan(graph1) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        });
        var sql2 = compiler.Compile(new Planner().Plan(graph2) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        });

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "CREATE TABLE contracts (id INTEGER NOT NULL, contract_type INTEGER NOT NULL, tenant_id INTEGER NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        var provider = new SqlExecutionProvider(connection);
        var context = new FoundgineExecutionContext(new Dictionary<string, object?> { ["user.TenantId"] = 7 });
        var first = await provider.ExecuteAsync(sql1, context);
        var second = await provider.ExecuteAsync(sql2, context);

        Assert.NotNull(first.Evidence);
        Assert.NotNull(second.Evidence);
        Assert.Equal(first.Evidence!.PlanFingerprint, second.Evidence!.PlanFingerprint);
    }

    [Fact]
    public async Task Public_engine_enriches_provider_evidence_with_intent_and_authorization_fingerprints()
    {
        var compiler = new CapturingEvidenceCompiler();
        var provider = new CapturingEvidenceProvider();
        var engine = new Foundgine.Runtime.FoundgineEngine(
            new Foundgine.Runtime.FoundgineOptions
            {
                Model = Banking.BankingSemanticModel.Build(),
                AuthorizationPolicy = new AllowAllSemanticAuthorizationPolicy()
            },
            compiler,
            provider);

        var request = new SemanticRequest(
            Banking.BankingSemanticModel.Customer,
            [new SemanticSelection(new FieldId(1), null, [])]);

        var result = await engine.ExecuteAsync(request);

        Assert.NotNull(result.Evidence);
        Assert.False(string.IsNullOrWhiteSpace(result.Evidence!.IntentFingerprint));
        Assert.False(string.IsNullOrWhiteSpace(result.Evidence.AuthorizationFingerprint));
        Assert.NotEqual(result.Evidence.IntentFingerprint, result.Evidence.AuthorizationFingerprint);
        Assert.NotNull(result.Receipt);
        Assert.Equal(result.Evidence.PlanFingerprint, result.Receipt!.PlanFingerprint);
        Assert.Equal(result.Evidence.Provider, result.Receipt.Provider);
        Assert.False(string.IsNullOrWhiteSpace(result.Receipt.ResultFingerprint));
    }

    private sealed class CapturingEvidenceCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
        IProviderSecurityConformanceEvaluator
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants =>
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id).ToArray();

        public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan) =>
            new(
                plan.Provider,
                ir.RequiredSecurityInvariants,
                ir.RequiredSecurityInvariants.Where(PreservedSecurityInvariants.Contains).ToArray(),
                Array.Empty<string>());


        public ProviderPlan Compile(ExecutionIR ir) => new TestProviderPlan();
    }

    private sealed class CapturingEvidenceProvider : IExecutionProvider
    {
        public Task<ExecutionResult> ExecuteAsync(
            ProviderPlan plan,
            FoundgineExecutionContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionResult(
                Array.Empty<ExecutionRow>(),
                Evidence: ExecutionEvidenceFactory.Create(
                    "test",
                    "plan",
                    Array.Empty<int>(),
                    0,
                    0)));
    }

    private sealed record TestProviderPlan() : ProviderPlan("test");
}