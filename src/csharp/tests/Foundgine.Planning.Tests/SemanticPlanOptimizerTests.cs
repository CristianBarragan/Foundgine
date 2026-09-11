using Foundgine.Core.Abstractions;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class SemanticPlanOptimizerTests
{
    [Fact]
    public void Equivalent_authorization_and_expressions_are_canonicalized()
    {
        var left = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId"));
        var right = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "Region"),
            AuthorizationPredicate.Constant("NZ"));

        var a = new SemanticPlan(new SemanticPlanNode(
            1,
            ExecutionOperation.Scan,
            new EntityId(1),
            [new FieldId(1)],
            null,
            null,
            [],
            Authorization: AuthorizationPredicate.And(left, right)));

        var b = new SemanticPlan(new SemanticPlanNode(
            1,
            ExecutionOperation.Scan,
            new EntityId(1),
            [new FieldId(1)],
            null,
            null,
            [],
            Authorization: AuthorizationPredicate.And(right, left)));

        var optimizer = new SemanticPlanOptimizer();
        var optimizedA = optimizer.Optimize(a).Plan;
        var optimizedB = optimizer.Optimize(b).Plan;

        Assert.Equal(
            SemanticPlanFingerprint.Create(optimizedA),
            SemanticPlanFingerprint.Create(optimizedB));
    }

    [Fact]
    public void Duplicate_authorization_terms_are_removed_without_changing_policy_shape()
    {
        var predicate = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId"));

        var plan = new SemanticPlan(new SemanticPlanNode(
            1,
            ExecutionOperation.Scan,
            new EntityId(1),
            [new FieldId(1)],
            null,
            null,
            [],
            Authorization: AuthorizationPredicate.And(predicate, predicate)));

        var result = new SemanticPlanOptimizer().Optimize(plan);

        Assert.True(result.Changed);
        Assert.Contains("authorization.canonicalization", result.AppliedRules);
        Assert.Same(predicate, result.Plan.Root.Authorization);
    }

    [Fact]
    public void Double_negation_is_removed()
    {
        var predicate = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId"));

        var plan = new SemanticPlan(new SemanticPlanNode(
            1,
            ExecutionOperation.Scan,
            new EntityId(1),
            [new FieldId(1)],
            null,
            null,
            [],
            Authorization: AuthorizationPredicate.Not(AuthorizationPredicate.Not(predicate))));

        var result = new SemanticPlanOptimizer().Optimize(plan);

        Assert.Same(predicate, result.Plan.Root.Authorization);
        Assert.Contains("authorization.canonicalization", result.AppliedRules);
    }

    [Fact]
    public void Optimization_never_removes_authorization_predicates()
    {
        var parent = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId"));
        var child = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "Region"),
            AuthorizationPredicate.Constant("NZ"));

        var childNode = new SemanticPlanNode(
            2,
            ExecutionOperation.Traverse,
            new EntityId(2),
            [new FieldId(2)],
            new RelationshipId(10),
            null,
            [],
            Authorization: child);

        var plan = new SemanticPlan(new SemanticPlanNode(
            1,
            ExecutionOperation.Scan,
            new EntityId(1),
            [new FieldId(1)],
            null,
            null,
            [childNode],
            Authorization: parent));

        var optimized = new SemanticPlanOptimizer().Optimize(plan).Plan;

        Assert.NotNull(optimized.Root.Authorization);
        Assert.NotNull(optimized.Root.Children[0].Authorization);
    }
}

// security-preservation coverage