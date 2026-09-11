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
/// Security invariants for provider-plan caching. The cache may reuse a compiled
/// plan across runtime contexts only when the plan itself contains the runtime
/// context lookup as a provider-independent predicate.
/// </summary>
public sealed class ContextSafePlanCacheTests
{
    [Fact]
    public async Task Same_authorized_shape_reuses_plan_across_runtime_contexts()
    {
        var compiler = new CountingCompiler();
        var engine = CreateEngine(compiler, new TenantPolicy());
        var request = CreateCustomerRequest();

        await engine.ExecuteAsync(request, new ExecutionContext(
            new Dictionary<string, object?> { ["user.TenantId"] = 7 }));
        await engine.ExecuteAsync(request, new ExecutionContext(
            new Dictionary<string, object?> { ["user.TenantId"] = 42 }));

        Assert.Equal(1, compiler.Count);
    }

    [Fact]
    public async Task Runtime_context_values_are_not_part_of_the_plan_cache_key()
    {
        var compiler = new CountingCompiler();
        var engine = CreateEngine(compiler, new TenantPolicy());
        var request = CreateCustomerRequest();

        await engine.ExecuteAsync(request, new ExecutionContext(
            new Dictionary<string, object?> { ["user.TenantId"] = 7 }));
        await engine.ExecuteAsync(request, new ExecutionContext(
            new Dictionary<string, object?> { ["user.TenantId"] = 8 }));

        Assert.Equal(1, compiler.Count);
    }

    [Fact]
    public async Task Different_authorization_predicates_do_not_share_a_provider_plan()
    {
        var compiler = new CountingCompiler();
        var cache = new MemoryProviderPlanCache();
        var model = Banking.BankingSemanticModel.Build();

        var first = new FoundgineEngine(new FoundgineOptions
        {
            Model = model,
            AuthorizationPolicy = new TenantPolicy(),
            PlanCache = cache
        }, compiler, new TestExecutionProvider());

        var second = new FoundgineEngine(new FoundgineOptions
        {
            Model = model,
            AuthorizationPolicy = new RegionPolicy(),
            PlanCache = cache
        }, compiler, new TestExecutionProvider());

        await first.ExecuteAsync(CreateCustomerRequest());
        await second.ExecuteAsync(CreateCustomerRequest());

        Assert.Equal(2, compiler.Count);
    }

    [Fact]
    public async Task Denied_requests_never_compile_or_read_a_cached_provider_plan()
    {
        var compiler = new CountingCompiler();
        var cache = new MemoryProviderPlanCache();
        var engine = CreateEngine(compiler, new DenyCustomerPolicy(), cache);

        await Assert.ThrowsAsync<SemanticAuthorizationException>(() => engine.ExecuteAsync(CreateCustomerRequest()));

        Assert.Equal(0, compiler.Count);
        Assert.False(cache.TryGet("unrelated", out _));
    }

    private static FoundgineEngine CreateEngine(
        CountingCompiler compiler,
        ISemanticAuthorizationPolicy policy,
        IProviderPlanCache? cache = null) =>
        new(
            new FoundgineOptions
            {
                Model = Banking.BankingSemanticModel.Build(),
                AuthorizationPolicy = policy,
                PlanCache = cache ?? new MemoryProviderPlanCache()
            },
            compiler,
            new TestExecutionProvider());

    private static SemanticRequest CreateCustomerRequest() =>
        new(
            Banking.BankingSemanticModel.Customer,
            [new SemanticSelection(new FieldId(1), null, [])],
            null);

    private sealed class TenantPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override AuthorizationPredicate? GetPredicate(EntityId entityId, AuthorizationOperation operation) =>
            operation == AuthorizationOperation.Read && entityId == Banking.BankingSemanticModel.Customer
                ? AuthorizationPredicate.Equal(
                    AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
                    AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId"))
                : null;
    }

    private sealed class RegionPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override AuthorizationPredicate? GetPredicate(EntityId entityId, AuthorizationOperation operation) =>
            operation == AuthorizationOperation.Read && entityId == Banking.BankingSemanticModel.Customer
                ? AuthorizationPredicate.Equal(
                    AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "RegionId"),
                    AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "RegionId"))
                : null;
    }

    private sealed class DenyCustomerPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override AuthorizationDecision GetEntityAccess(EntityId entityId, AuthorizationOperation operation) =>
            entityId == Banking.BankingSemanticModel.Customer && operation == AuthorizationOperation.Read
                ? AuthorizationDecision.Denied
                : AuthorizationDecision.Allowed;
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
        public Task<ExecutionResult> ExecuteAsync(ProviderPlan plan, ExecutionContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionResult(Array.Empty<ExecutionRow>()));
    }

    private sealed record TestPlan() : ProviderPlan("test");
}

public sealed class WarrantPlanCacheAttackTests
{
    [Fact]
    public void Different_warrants_cannot_share_an_authority_cache_key()
    {
        var now = DateTimeOffset.UtcNow;
        var first = new Foundgine.Core.Semantic.Security.Warrants.SecurityWarrant(
            "w1", "issuer", "agent-a", "foundgine",
            [new Foundgine.Core.Semantic.Security.Warrants.CapabilityGrant("Customer.read", "read")],
            Foundgine.Core.Semantic.Security.Warrants.SecurityWarrantConstraints.Unrestricted,
            now.AddMinutes(-1), now.AddHours(1), "n1", "k1", null, []);
        var second = first with { Id = "w2", Nonce = "n2" };

        var cache = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [first.Digest] = "authorized-plan-for-w1"
        };

        Assert.True(cache.ContainsKey(first.Digest));
        Assert.False(cache.ContainsKey(second.Digest));
    }
}