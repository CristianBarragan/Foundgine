using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Security;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class SecurityPreservingRewriteTests
{
    [Fact]
    public void Optimization_preserves_the_complete_security_contract()
    {
        var predicate = AuthorizationPredicate.And(
            AuthorizationPredicate.Equal(
                AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
                AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId")),
            AuthorizationPredicate.Equal(
                AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "Region"),
                AuthorizationPredicate.Constant("NZ")));

        var plan = new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [],
                Authorization: predicate),
            [
                SecurityInvariantIds.AuthorizationRequired, SecurityInvariantIds.RuntimeAuthorization,
                SecurityInvariantIds.TenantIsolation, SecurityInvariantIds.ParameterizedValues,
                SecurityInvariantIds.PlanCacheContextIsolation
            ]);

        var result = new SemanticPlanOptimizer().Optimize(plan);

        Assert.True(result.SecurityProof.IsSatisfied);
        Assert.Empty(result.SecurityProof.Missing);
        Assert.Equal(plan.EffectiveSecurityInvariants.OrderBy(x => x), result.SecurityProof.After.OrderBy(x => x));
    }

    [Fact]
    public void Security_requirements_participate_in_before_and_after_fingerprints()
    {
        var a = new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, []),
            [SecurityInvariantIds.AuthorizationRequired]);
        var b = new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, []),
            [SecurityInvariantIds.TenantIsolation]);

        Assert.NotEqual(SemanticPlanFingerprint.Create(a), SemanticPlanFingerprint.Create(b));
    }

    [Fact]
    public void Rewrite_proof_rejects_a_plan_that_drops_an_invariant()
    {
        var before = new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, []),
            [SecurityInvariantIds.AuthorizationRequired, SecurityInvariantIds.TenantIsolation]);
        var after = new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, []),
            [SecurityInvariantIds.AuthorizationRequired]);

        var exception = Assert.Throws<InvalidOperationException>(() => SecurityPreservationProof.Create(before, after));
        Assert.Contains(SecurityInvariantIds.TenantIsolation, exception.Message);
    }
}