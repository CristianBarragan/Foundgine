using Foundgine.Core.Abstractions;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class ProviderAwareCostSelectionTests
{
    private static SemanticPlan Plan() => new(new SemanticPlanNode(
        1,
        ExecutionOperation.Scan,
        new EntityId(1),
        [new FieldId(1)],
        null,
        null,
        [],
        Authorization: AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId"))));

    [Fact]
    public void Provider_cost_can_change_rule_selection()
    {
        var cheap = new SelectionRule("test.cheap", benefit: 5d, cost: 0d);
        var expensive = new SelectionRule("test.expensive", benefit: 8d, cost: 0d);
        var estimator = new FakeProviderCostEstimator((_, _, rule) =>
            rule.Name == "test.expensive"
                ? ProviderCostEstimate.From("test-provider", 100d, 10d, 0.9d)
                : ProviderCostEstimate.From("test-provider", 1d, 10d, 0.9d));

        var selector = new RewriteRuleSelector(
            providerCostEstimator: estimator,
            providerPolicy: new ProviderCostSelectionPolicy(ProviderCostWeight: 1d));

        var selected = selector.SelectProviderAware(Plan(), [cheap, expensive]);

        Assert.NotNull(selected);
        Assert.Equal("test.cheap", selected!.RuleName);
        Assert.Equal("test-provider", selected.Provider);
        Assert.Equal(1d, selected.EstimatedExecutionCost);
    }

    [Fact]
    public void Provider_cost_is_advisory_and_does_not_replace_proof_checks()
    {
        var rule = new SelectionRule("test.invalid", benefit: 1000d, cost: 0d, changesMeaning: true);
        var estimator = new FakeProviderCostEstimator((_, _, _) =>
            ProviderCostEstimate.From("test-provider", 0d, 1d, 1d));

        var composition = new RewriteRuleComposer(
            [rule],
            new RewriteRuleCompositionOptions(ProviderCostEstimator: estimator));

        Assert.Throws<InvalidOperationException>(() => composition.Compose(Plan()));
    }

    [Fact]
    public void Provider_selection_history_contains_estimate_and_score()
    {
        var rule = new SelectionRule("test.rule", benefit: 4d, cost: 2d);
        var estimator = new FakeProviderCostEstimator((_, _, _) =>
            ProviderCostEstimate.From("test-provider", 3d, 12d, 0.75d));

        var result = new RewriteRuleComposer(
            [rule],
            new RewriteRuleCompositionOptions(ProviderCostEstimator: estimator)).Compose(Plan());

        var candidate = Assert.Single(result.ProviderCandidates);
        Assert.Equal("test-provider", candidate.Provider);
        Assert.Equal(3d, candidate.EstimatedExecutionCost);
        Assert.Equal(12d, candidate.EstimatedRows);
        Assert.Equal(0.75d, candidate.CostConfidence);
        Assert.True(candidate.Score > 0d);
    }

    private sealed class FakeProviderCostEstimator(
        Func<SemanticPlan, SemanticPlan, IPlanRewriteRule, ProviderCostEstimate> estimate) : IProviderCostEstimator
    {
        public string Provider => "test-provider";

        public ProviderCostEstimate Estimate(SemanticPlan before, SemanticPlan candidate, IPlanRewriteRule rule) =>
            estimate(before, candidate, rule);
    }

    private sealed class SelectionRule(
        string name,
        double benefit,
        double cost,
        bool changesMeaning = false) : IPlanRewriteRule
    {
        public string Name => name;
        public IReadOnlyList<string> Preconditions => ["test plan"];
        public IReadOnlyList<string> SecurityObligations => ["authorization.required"];
        public double CostImpact => cost;
        public double BenefitEstimate => benefit;
        public bool CanApply(SemanticPlan plan) => true;

        public SemanticPlan Apply(SemanticPlan plan) => changesMeaning
            ? plan with { Root = plan.Root with { Fields = [new FieldId(99)] } }
            : plan;
    }
}