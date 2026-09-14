using Foundgine.Core.Abstractions;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class RewriteRuleCompositionTests
{
    private static SemanticPlan Plan() => new(new SemanticPlanNode(
        1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [],
        Authorization: AuthorizationPredicate.Not(AuthorizationPredicate.Not(
            AuthorizationPredicate.Equal(
                AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
                AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId"))))));

    [Fact]
    public void Composer_respects_dependency_order()
    {
        var first = new NamedRule("test.first", priority: 10);
        var second = new NamedRule("test.second", after: ["test.first"], priority: -10);
        var result = new RewriteRuleComposer([second, first]).Compose(Plan());

        Assert.Equal(["test.first", "test.second"], result.AppliedRules);
        Assert.True(result.TerminatedNormally);
    }

    [Fact]
    public void Composer_rejects_ordering_cycles()
    {
        var a = new NamedRule("test.a", after: ["test.b"]);
        var b = new NamedRule("test.b", after: ["test.a"]);

        Assert.Throws<InvalidOperationException>(() => new RewriteRuleComposer([a, b]));
    }

    [Fact]
    public void Composer_rejects_unknown_dependencies()
    {
        var a = new NamedRule("test.a", after: ["missing"]);

        Assert.Throws<InvalidOperationException>(() => new RewriteRuleComposer([a]));
    }

    [Fact]
    public void Composer_rejects_mutual_conflicts()
    {
        var a = new NamedRule("test.a", conflicts: ["test.b"]);
        var b = new NamedRule("test.b", conflicts: ["test.a"]);

        Assert.Throws<InvalidOperationException>(() => new RewriteRuleComposer([a, b]));
    }

    [Fact]
    public void Idempotent_rule_is_applied_at_most_once()
    {
        var rule = new NamedRule("test.idempotent");
        var result = new RewriteRuleComposer([rule]).Compose(Plan());

        Assert.Single(result.Applications);
        Assert.Equal(5d, result.TotalCostImpact);
    }

    [Fact]
    public void Composition_detects_non_idempotent_cycles_by_plan_fingerprint()
    {
        var rule = new OscillatingRule();

        Assert.Throws<InvalidOperationException>(() => new RewriteRuleComposer(
            [rule], new RewriteRuleCompositionOptions(MaxRuleApplications: 8, MaxPlanVisits: 8)).Compose(Plan()));
    }

    [Fact]
    public void Composition_accumulates_cost_and_preserves_proofs()
    {
        var first = new NamedRule("test.first", cost: 1.5);
        var second = new NamedRule("test.second", after: ["test.first"], cost: 2.5);
        var result = new RewriteRuleComposer([first, second]).Compose(Plan());

        Assert.Equal(4d, result.TotalCostImpact);
        Assert.All(result.Applications, application =>
        {
            Assert.True(application.SecurityProof.IsSatisfied);
            Assert.True(application.SemanticProof.IsSatisfied);
        });
    }

    private sealed class NamedRule : IPlanRewriteRule
    {
        private readonly string _name;
        private readonly IReadOnlyList<string> _after;
        private readonly IReadOnlyList<string> _conflicts;
        private readonly double _cost;

        public NamedRule(string name, IReadOnlyList<string>? after = null, IReadOnlyList<string>? conflicts = null,
            double cost = 5d, int priority = 0)
        {
            _name = name;
            _after = after ?? [];
            _conflicts = conflicts ?? [];
            _cost = cost;
            Priority = priority;
        }

        public string Name => _name;
        public IReadOnlyList<string> Preconditions => ["test plan"];
        public IReadOnlyList<string> SecurityObligations => ["authorization.required"];
        public double CostImpact => _cost;
        public IReadOnlyList<string> MustRunAfter => _after;
        public IReadOnlyList<string> ConflictsWith => _conflicts;
        public int Priority { get; }
        public bool CanApply(SemanticPlan plan) => true;

        public SemanticPlan Apply(SemanticPlan plan) => plan with
        {
            Root = plan.Root with
            {
                Authorization = AuthorizationPredicate.Not(AuthorizationPredicate.Not(plan.Root.Authorization!))
            }
        };
    }

    private sealed class OscillatingRule : IPlanRewriteRule
    {
        private bool _toggle;
        public string Name => "test.oscillating";
        public IReadOnlyList<string> Preconditions => ["test plan"];
        public IReadOnlyList<string> SecurityObligations => ["authorization.required"];
        public double CostImpact => 1d;
        public bool IsIdempotent => false;
        public bool CanApply(SemanticPlan plan) => true;

        public SemanticPlan Apply(SemanticPlan plan)
        {
            _toggle = !_toggle;
            return plan with { Root = plan.Root with { Id = _toggle ? 2 : 1 } };
        }
    }
}

public sealed class RewriteRuleSelectionTests
{
    private static SemanticPlan SelectionPlan() => new(new SemanticPlanNode(
        1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [],
        Authorization: AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId"))));

    [Xunit.Fact]
    public void Selector_prefers_higher_benefit_when_cost_is_equal()
    {
        var low = new SelectionRule("low", benefit: 1d, cost: 1d, priority: 10);
        var high = new SelectionRule("high", benefit: 3d, cost: 1d, priority: -10);
        var selected = new RewriteRuleSelector().Select(SelectionPlan(), [low, high]);

        Xunit.Assert.Equal("high", selected!.RuleName);
        Xunit.Assert.Equal(1.5d, selected.Score);
    }

    [Xunit.Fact]
    public void Selector_penalizes_expensive_rewrites()
    {
        var cheap = new SelectionRule("cheap", benefit: 2d, cost: 0d);
        var expensive = new SelectionRule("expensive", benefit: 10d, cost: 10d);
        var selected = new RewriteRuleSelector().Select(SelectionPlan(), [cheap, expensive]);

        Xunit.Assert.Equal("cheap", selected!.RuleName);
    }

    [Xunit.Fact]
    public void Composer_records_selection_history()
    {
        var low = new SelectionRule("test.low", benefit: 1d, cost: 1d, priority: 0);
        var high = new SelectionRule("test.high", benefit: 4d, cost: 1d, priority: 0);
        var result = new RewriteRuleComposer([low, high]).Compose(SelectionPlan());

        Xunit.Assert.NotEmpty(result.Candidates);
        Xunit.Assert.Equal("test.high", result.Candidates[0].RuleName);
    }

    private sealed class SelectionRule : IPlanRewriteRule
    {
        private readonly string _name;
        private readonly double _benefit;
        private readonly double _cost;

        public SelectionRule(string name, double benefit, double cost, int priority = 0)
        {
            _name = name;
            _benefit = benefit;
            _cost = cost;
            Priority = priority;
        }

        public string Name => _name;
        public IReadOnlyList<string> Preconditions => ["selection test plan"];
        public IReadOnlyList<string> SecurityObligations => ["authorization.required"];
        public double CostImpact => _cost;
        public double BenefitEstimate => _benefit;
        public int Priority { get; }
        public bool CanApply(SemanticPlan plan) => true;
        public SemanticPlan Apply(SemanticPlan plan) => plan;
    }
}