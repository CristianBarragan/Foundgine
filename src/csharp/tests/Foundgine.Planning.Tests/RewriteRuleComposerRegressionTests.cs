using Foundgine.Core.Abstractions;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class RewriteRuleComposerRegressionTests
{
    [Fact]
    public void Equivalent_rebuilt_plan_is_not_reported_as_a_cycle()
    {
        var plan = new SemanticPlan(new SemanticPlanNode(
            1,
            ExecutionOperation.Scan,
            new EntityId(1),
            [new FieldId(1)],
            null,
            null,
            [],
            null));

        var composer = new RewriteRuleComposer([new EquivalentRebuildRule()]);

        var result = composer.Compose(plan);

        Assert.True(result.TerminatedNormally);
        Assert.Empty(result.Applications);
    }

    private sealed class EquivalentRebuildRule : IPlanRewriteRule
    {
        public string Name => "test.equivalent-rebuild";
        public IReadOnlyList<string> Preconditions => [];
        public IReadOnlyList<string> SecurityObligations => [];
        public double CostImpact => 0;
        public double BenefitEstimate => 1;
        public bool IsIdempotent => true;
        public int Priority => 0;
        public bool CanApply(SemanticPlan plan) => true;

        public SemanticPlan Apply(SemanticPlan plan)
        {
            var root = plan.Root with
            {
                Fields = plan.Root.Fields.ToArray()
            };
            return new SemanticPlan(root, plan.RequiredSecurityInvariants);
        }
    }
}