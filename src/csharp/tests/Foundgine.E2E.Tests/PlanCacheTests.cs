using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Security;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Runtime;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

/// <summary>
///  proves that provider compilation may be cached without bypassing
/// semantic authorization or removing runtime authorization predicates.
/// </summary>
public sealed class PlanCacheTests
{
    [Fact]
    public async Task Repeated_authorized_request_reuses_compiled_provider_plan()
    {
        var compiler = new CountingCompiler();
        var cache = new MemoryProviderPlanCache();
        var engine = new FoundgineEngine(
            new FoundgineOptions
            {
                Model = Banking.BankingSemanticModel.Build(),
                AuthorizationPolicy = new TenantPolicy(),
                PlanCache = cache
            },
            compiler,
            new TestExecutionProvider());

        var request = CreateCustomerRequest();

        await engine.ExecuteAsync(request, new ExecutionContext(
            new Dictionary<string, object?> { ["user.TenantId"] = 7 }));
        await engine.ExecuteAsync(request, new ExecutionContext(
            new Dictionary<string, object?> { ["user.TenantId"] = 42 }));

        Assert.Equal(1, compiler.Count);
    }

    [Fact]
    public async Task Authorization_is_still_evaluated_before_cache_lookup()
    {
        var compiler = new CountingCompiler();
        var policy = new CountingPolicy();
        var engine = new FoundgineEngine(
            new FoundgineOptions
            {
                Model = Banking.BankingSemanticModel.Build(),
                AuthorizationPolicy = policy,
                PlanCache = new MemoryProviderPlanCache()
            },
            compiler,
            new TestExecutionProvider());

        var request = CreateCustomerRequest();
        var checksBeforeExecution = policy.EntityReadChecks;

        await engine.ExecuteAsync(request);
        await engine.ExecuteAsync(request);

        Assert.Equal(checksBeforeExecution + 2, policy.EntityReadChecks);
        Assert.Equal(1, compiler.Count);
    }

    [Fact]
    public async Task Different_request_values_do_not_share_an_exact_plan_cache_entry()
    {
        var compiler = new CountingCompiler();
        var engine = new FoundgineEngine(
            new FoundgineOptions
            {
                Model = Banking.BankingSemanticModel.Build(),
                AuthorizationPolicy = new TenantPolicy(),
                PlanCache = new MemoryProviderPlanCache()
            },
            compiler,
            new TestExecutionProvider());

        await engine.ExecuteAsync(CreateCustomerRequest(7));
        await engine.ExecuteAsync(CreateCustomerRequest(42));

        Assert.Equal(2, compiler.Count);
    }

    private static SemanticRequest CreateCustomerRequest(int? tenantId = null)
    {
        var options = tenantId is null
            ? null
            : new Foundgine.Core.Semantic.Query.SemanticQueryOptions(
                Filter: new Foundgine.Core.Semantic.Query.SemanticFieldFilter(
                    new FieldId(1),
                    Foundgine.Core.Semantic.Query.SemanticFilterOperator.Eq,
                    tenantId));

        return new SemanticRequest(
            Banking.BankingSemanticModel.Customer,
            [new SemanticSelection(new FieldId(1), null, [])],
            options);
    }

    private sealed class TenantPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override AuthorizationPredicate? GetPredicate(
            EntityId entityId,
            AuthorizationOperation operation) =>
            operation == AuthorizationOperation.Read && entityId == Banking.BankingSemanticModel.Customer
                ? AuthorizationPredicate.Equal(
                    AuthorizationPredicate.Member(
                        AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
                    AuthorizationPredicate.Member(
                        AuthorizationPredicate.ContextParameter("user"), "TenantId"))
                : null;
    }

    private sealed class CountingPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public int EntityReadChecks { get; private set; }

        public override AuthorizationDecision GetEntityAccess(
            EntityId entityId,
            AuthorizationOperation operation)
        {
            if (operation == AuthorizationOperation.Read)
                EntityReadChecks++;

            return AuthorizationDecision.Allowed;
        }
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

    private sealed class TestExecutionProvider : IExecutionProvider
    {
        public Task<ExecutionResult> ExecuteAsync(
            ProviderPlan plan,
            ExecutionContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionResult(Array.Empty<ExecutionRow>()));
    }

    private sealed record TestPlan() : ProviderPlan("test");
}