using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Security;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class SemanticEquivalenceProofTests
{
    [Fact]
    public void Optimizer_produces_a_satisfied_semantic_equivalence_proof()
    {
        var predicate = AuthorizationPredicate.And(
            AuthorizationPredicate.Equal(
                AuthorizationPredicate.ResourceParameter("tenant"),
                AuthorizationPredicate.Constant("nz")),
            AuthorizationPredicate.Equal(
                AuthorizationPredicate.ResourceParameter("region"),
                AuthorizationPredicate.Constant("north")));

        var plan = new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [],
                Authorization: predicate),
            [SecurityInvariantIds.AuthorizationRequired]);

        var result = new SemanticPlanOptimizer().Optimize(plan);

        Assert.True(result.SemanticProof.IsSatisfied);
        Assert.Equal(
            SemanticEquivalenceFingerprint.Create(plan),
            result.SemanticProof.BeforeFingerprint);
        Assert.Equal(
            SemanticEquivalenceFingerprint.Create(result.Plan),
            result.SemanticProof.AfterFingerprint);
    }

    [Fact]
    public void Authorization_operand_reordering_is_semantically_equivalent()
    {
        var a = AuthorizationPredicate.Equal(
            AuthorizationPredicate.ResourceParameter("a"),
            AuthorizationPredicate.Constant("1"));
        var b = AuthorizationPredicate.Equal(
            AuthorizationPredicate.ResourceParameter("b"),
            AuthorizationPredicate.Constant("2"));

        var first = CreatePlan(AuthorizationPredicate.And(a, b));
        var second = CreatePlan(AuthorizationPredicate.And(b, a));

        var proof = SemanticEquivalenceProof.Create(first, second);

        Assert.True(proof.IsSatisfied);
    }

    [Fact]
    public void Meaningful_field_change_is_not_semantically_equivalent()
    {
        var first = CreatePlan(null, new FieldId(1));
        var second = CreatePlan(null, new FieldId(2));

        Assert.Throws<InvalidOperationException>(() => SemanticEquivalenceProof.Create(first, second));
    }

    [Fact]
    public void Meaningful_pagination_change_is_not_semantically_equivalent()
    {
        var first = CreatePlan(null, new FieldId(1), new SemanticQueryOptions(Limit: 10));
        var second = CreatePlan(null, new FieldId(1), new SemanticQueryOptions(Limit: 20));

        Assert.Throws<InvalidOperationException>(() => SemanticEquivalenceProof.Create(first, second));
    }

    [Fact]
    public void Security_contract_change_is_not_semantically_equivalent()
    {
        var first = new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, []),
            [SecurityInvariantIds.AuthorizationRequired]);
        var second = new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, []),
            [SecurityInvariantIds.TenantIsolation]);

        Assert.Throws<InvalidOperationException>(() => SemanticEquivalenceProof.Create(first, second));
    }

    private static SemanticPlan CreatePlan(
        AuthorizationPredicate? authorization,
        FieldId? field = null,
        SemanticQueryOptions? options = null)
    {
        return new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, new EntityId(1), [field ?? new FieldId(1)], null, null, [],
                options, authorization));
    }
}