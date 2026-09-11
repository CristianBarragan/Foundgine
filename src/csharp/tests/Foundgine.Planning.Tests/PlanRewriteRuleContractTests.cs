using Foundgine.Core.Abstractions;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed partial class PlanRewriteRuleContractTests
{
    private static SemanticPlan Plan(AuthorizationPredicate? authorization)
        => new(new SemanticPlanNode(
            1,
            ExecutionOperation.Scan,
            new EntityId(1),
            [new FieldId(1)],
            null,
            null,
            [],
            Authorization: authorization));

    [Fact]
    public void Authorization_rule_exposes_auditable_contract()
    {
        var rule = new AuthorizationCanonicalizationRule();

        Assert.Equal("authorization.canonicalization", rule.Name);
        Assert.NotEmpty(rule.Preconditions);
        Assert.Contains("authorization.required", rule.SecurityObligations);
        Assert.Contains("authorization.runtime", rule.SecurityObligations);
        Assert.Equal(0d, rule.CostImpact);
    }

    [Fact]
    public void Rule_application_produces_both_proofs()
    {
        var predicate = AuthorizationPredicate.And(
            AuthorizationPredicate.Equal(
                AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
                AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId")),
            AuthorizationPredicate.Equal(
                AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "Region"),
                AuthorizationPredicate.Constant("NZ")));
        var before = Plan(predicate);
        var rule = new AuthorizationCanonicalizationRule();
        var after = rule.Apply(before);

        var result = SemanticPlanOptimizer.ApplyRule(rule, before, after);

        Assert.True(result.IsSatisfied);
        Assert.True(result.SecurityProof.IsSatisfied);
        Assert.True(result.SemanticProof.IsSatisfied);
    }

    [Fact]
    public void Optimizer_uses_rule_contract_and_records_rule_name()
    {
        var predicate = AuthorizationPredicate.Not(
            AuthorizationPredicate.Not(
                AuthorizationPredicate.Equal(
                    AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
                    AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId"))));
        var result = new SemanticPlanOptimizer().Optimize(Plan(predicate));

        Assert.Contains("authorization.canonicalization", result.AppliedRules);
        Assert.True(result.SecurityProof.IsSatisfied);
        Assert.True(result.SemanticProof.IsSatisfied);
    }

    [Fact]
    public void Rule_cannot_be_applied_without_its_precondition()
    {
        var rule = new AuthorizationCanonicalizationRule();
        var plan = Plan(null);

        Assert.False(rule.CanApply(plan));
        Assert.Same(plan, rule.Apply(plan));
    }

    [Fact]
    public void Custom_rule_with_changed_meaning_is_rejected_by_contract()
    {
        var rule = new MeaningChangingRule();
        var before = Plan(AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId")));
        var after = rule.Apply(before);

        Assert.Throws<InvalidOperationException>(() => SemanticPlanOptimizer.ApplyRule(rule, before, after));
    }

    private sealed class MeaningChangingRule : IPlanRewriteRule
    {
        public string Name => "test.meaning-changing";
        public IReadOnlyList<string> Preconditions => ["test"];
        public IReadOnlyList<string> SecurityObligations => ["authorization.required"];
        public double CostImpact => 100d;
        public bool CanApply(SemanticPlan plan) => true;

        public SemanticPlan Apply(SemanticPlan plan) => plan with
        {
            Root = plan.Root with { Fields = [new FieldId(99)] }
        };
    }
}

// M3 proof-carrying optimizer contract coverage.
public sealed partial class PlanRewriteRuleContractTests
{
    [Fact]
    public void Rule_application_carries_security_obligation_proof()
    {
        var before = SecurityInvariantPlanRequirements.Attach(Plan(null));
        var rule = new ProjectionPruningRule();
        var after = rule.Apply(before);

        var result = SemanticPlanOptimizer.ApplyRule(rule, before, after);

        Assert.True(result.SecurityObligationProof.IsSatisfied);
        Assert.Contains("visibility.field", result.SecurityObligationProof.Preserved);
        Assert.Contains("authorization.required", result.SecurityObligationProof.Preserved);
    }

    [Fact]
    public void Unknown_security_obligation_is_rejected_fail_closed()
    {
        var rule = new UnknownObligationRule();
        var plan = SecurityInvariantPlanRequirements.Attach(Plan(null));

        Assert.Throws<InvalidOperationException>(() =>
            SemanticPlanOptimizer.ApplyRule(rule, plan, plan));
    }

    private sealed class UnknownObligationRule : IPlanRewriteRule
    {
        public string Name => "test.unknown-obligation";
        public IReadOnlyList<string> Preconditions => [];
        public IReadOnlyList<string> SecurityObligations => ["security.this-does-not-exist"];
        public double CostImpact => 0d;
        public bool CanApply(SemanticPlan plan) => true;
        public SemanticPlan Apply(SemanticPlan plan) => plan;
    }
}