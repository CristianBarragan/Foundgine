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

// <summary> proves granular semantic authorization, capability discovery,
//and the public API boundary without requiring GraphQL or SQL.</summary>
public sealed class AuthorizationCapabilityTests
{
    [Fact]
    public void Public_facade_exposes_policy_scoped_capabilities_for_callers()
    {
        var model = Banking.BankingSemanticModel.Build();
        var policy = new ReadOnlyCustomerPolicy();
        var engine = new FoundgineEngine(
            new FoundgineOptions
            {
                Model = model,
                AuthorizationPolicy = policy
            },
            new TestProviderPlanCompiler(),
            new TestExecutionProvider());

        var capabilities = engine.DescribeCapabilities();
        var customer = capabilities.Entities.Single(x => x.EntityId == Banking.BankingSemanticModel.Customer);

        Assert.Equal(AuthorizationAccess.Allowed, customer.Read.Access);
        Assert.Equal(AuthorizationAccess.Denied, customer.Write.Access);
        Assert.Contains(customer.Fields, x =>
            x.Name == "Name" && x.Read.Access == AuthorizationAccess.Allowed);
    }

    private sealed class ReadOnlyCustomerPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanWriteEntity(EntityId entityId) => false;
        public override bool CanWriteField(EntityId entityId, FieldId fieldId) => false;
    }

    private sealed class TestProviderPlanCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
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

        public ProviderPlan Compile(ExecutionIR ir) => new TestPlan();
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