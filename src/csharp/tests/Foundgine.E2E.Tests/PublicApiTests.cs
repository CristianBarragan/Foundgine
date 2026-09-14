using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Planning;
using Microsoft.Extensions.DependencyInjection;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Security;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Runtime;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

/// <summary>: the application-facing facade must not require callers to manually
/// orchestrate resolution, authorization, planning, or provider compilation.</summary>
public sealed class PublicApiTests
{
    [Fact]
    public void Plain_foundgine_has_no_optional_capabilities_enabled_by_default()
    {
        var options = new FoundgineOptions();

        Assert.Empty(options.EnabledCapabilities);
    }

    [Fact]
    public async Task Public_facade_executes_the_core_pipeline_through_di()
    {
        var model = Banking.BankingSemanticModel.Build();
        var policy = new AllowAllSemanticAuthorizationPolicy();
        var compiler = new TestProviderPlanCompiler();
        var provider = new TestExecutionProvider();

        var services = new ServiceCollection();
        services.AddSingleton<IProviderPlanCompiler>(compiler);
        services.AddSingleton<IExecutionProvider>(provider);
        services.AddFoundgine(model, policy);

        var engine = services.BuildServiceProvider().GetRequiredService<IFoundgine>();

        var request = new SemanticRequest(
            Banking.BankingSemanticModel.Customer,
            [new SemanticSelection(new FieldId(2), null, [])]);

        var result = await engine.ExecuteAsync(request);

        Assert.Single(result.Rows);
        Assert.Equal(1, compiler.CompiledCount);
        Assert.Equal(1, provider.ExecutionCount);
        Assert.Equal("Alice", result.Rows[0].Values["Name"]);
    }

    private sealed class TestProviderPlanCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
        IProviderSecurityConformanceEvaluator
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants =>
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id).ToArray();

        public int CompiledCount { get; private set; }

        public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan) =>
            new(
                plan.Provider,
                ir.RequiredSecurityInvariants,
                ir.RequiredSecurityInvariants.Where(PreservedSecurityInvariants.Contains).ToArray(),
                Array.Empty<string>());


        public ProviderPlan Compile(ExecutionIR ir)
        {
            CompiledCount++;
            return new TestPlan();
        }
    }

    private sealed class TestExecutionProvider : IExecutionProvider
    {
        public int ExecutionCount { get; private set; }

        public Task<ExecutionResult> ExecuteAsync(
            ProviderPlan plan,
            ExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(new ExecutionResult(
            [
                new ExecutionRow(new Dictionary<string, object?>
                {
                    ["Name"] = "Alice"
                })
            ]));
        }
    }

    private sealed record TestPlan() : ProviderPlan("test");
}