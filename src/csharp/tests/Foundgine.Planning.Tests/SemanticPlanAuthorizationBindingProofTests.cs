using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Security;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class SemanticPlanAuthorizationBindingProofTests
{
    [Fact]
    public void Rewrite_must_preserve_authorization_binding()
    {
        var binding = new SemanticPlanAuthorizationBinding("contract-a", "authorization-a");
        var before = CreatePlan(binding);
        var after = CreatePlan(null);

        Assert.Throws<InvalidOperationException>(() =>
            SemanticPlanAuthorizationBindingProof.Create(before, after));
    }

    [Fact]
    public void Rewrite_cannot_replace_authorization_binding()
    {
        var before = CreatePlan(new SemanticPlanAuthorizationBinding("contract-a", "authorization-a"));
        var after = CreatePlan(new SemanticPlanAuthorizationBinding("contract-b", "authorization-b"));

        Assert.Throws<InvalidOperationException>(() =>
            SemanticPlanAuthorizationBindingProof.Create(before, after));
    }

    [Fact]
    public void Unbound_plans_remain_unbound_during_optimization()
    {
        var plan = CreatePlan(null);

        var result = new SemanticPlanOptimizer().Optimize(plan);

        Assert.True(result.AuthorizationBindingProof.IsSatisfied);
        Assert.Null(result.Plan.AuthorizationBinding);
    }

    private static SemanticPlan CreatePlan(SemanticPlanAuthorizationBinding? binding)
    {
        return new SemanticPlan(
            new SemanticPlanNode(
                1,
                ExecutionOperation.Scan,
                new EntityId(1),
                [new FieldId(1)],
                null,
                null,
                []),
            [SecurityInvariantIds.AuthorizationRequired],
            binding);
    }
}
