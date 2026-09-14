using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Security;
using Foundgine.Runtime;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

/// <summary>
/// The repository-level authorization proof: capability discovery is
/// descriptive, authorization is applied again to the semantic graph, the
/// conditional predicate survives planning, and the provider receives the
/// predicate rather than a pre-authorized result.
/// </summary>
public sealed class AuthorizationGoldenPathTests
{
    [Fact]
    public void Capability_discovery_does_not_authorize_execution()
    {
        var model = Banking.BankingSemanticModel.Build();
        var policy = new TenantPolicy();
        var compiler = new CapturingCompiler();
        var provider = new CapturingProvider();
        var engine = new FoundgineEngine(
            new FoundgineOptions { Model = model, AuthorizationPolicy = policy },
            compiler,
            provider);

        var capabilities = engine.DescribeCapabilities();
        Assert.Contains(capabilities.Entities, x => x.EntityId == Banking.BankingSemanticModel.Customer);

        // Discovery does not invoke the provider or compile an execution plan.
        // The actual request below still passes through SemanticAuthorizer.
        Assert.Null(compiler.IR);
    }

    [Fact]
    public async Task Conditional_authorization_survives_semantic_pipeline()
    {
        var policy = new TenantPolicy();
        var compiler = new CapturingCompiler();
        var provider = new CapturingProvider();
        var engine = new FoundgineEngine(
            new FoundgineOptions { Model = Banking.BankingSemanticModel.Build(), AuthorizationPolicy = policy },
            compiler,
            provider);

        var entityChecksBeforeExecution = policy.EntityChecks;

        var request = new SemanticRequest(
            Banking.BankingSemanticModel.Customer,
            [new SemanticSelection(new FieldId(1), null, [])]);

        await engine.ExecuteAsync(request, new ExecutionContext(
            new Dictionary<string, object?> { ["user.TenantId"] = 7 }));

        Assert.NotNull(compiler.IR);
        Assert.NotNull(compiler.IR!.Root.Authorization);
        Assert.Equal(AuthorizationPredicateKind.Equal, compiler.IR.Root.Authorization!.Kind);
        Assert.NotNull(provider.Context);
        Assert.Equal(7, provider.Context!.EffectiveValues["user.TenantId"]);
        Assert.Equal(entityChecksBeforeExecution + 1, policy.EntityChecks);
    }

    private sealed class TenantPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public int EntityChecks { get; private set; }

        public override bool CanAccessEntity(EntityId entityId)
        {
            EntityChecks++;
            return true;
        }

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

    private sealed class CapturingCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
        IProviderSecurityConformanceEvaluator
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants =>
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id).ToArray();

        public ExecutionIR? IR { get; private set; }

        public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan) =>
            new(
                plan.Provider,
                ir.RequiredSecurityInvariants,
                ir.RequiredSecurityInvariants.Where(PreservedSecurityInvariants.Contains).ToArray(),
                Array.Empty<string>());


        public ProviderPlan Compile(ExecutionIR ir)
        {
            IR = ir;
            return new TestPlan();
        }
    }

    private sealed class CapturingProvider : IExecutionProvider
    {
        public ExecutionContext? Context { get; private set; }

        public Task<ExecutionResult> ExecuteAsync(
            ProviderPlan plan,
            ExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Context = context;
            return Task.FromResult(new ExecutionResult(Array.Empty<ExecutionRow>()));
        }
    }

    private sealed record TestPlan() : ProviderPlan("test");
}