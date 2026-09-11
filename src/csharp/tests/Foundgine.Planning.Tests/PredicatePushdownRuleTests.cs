using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class PredicatePushdownRuleTests
{
    [Fact]
    public void Distributes_conjunct_into_or_branches()
    {
        var a = new SemanticFieldFilter(new FieldId(1), SemanticFilterOperator.Eq, "A");
        var b = new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.Eq, "B");
        var c = new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "C");

        var filter = new SemanticAndFilter([
            new SemanticOrFilter([a, b]),
            c
        ]);

        var plan = CreatePlan(filter);
        var result = new PredicatePushdownRule().Apply(plan);

        var rewritten = Assert.IsType<SemanticOrFilter>(result.Root.QueryOptions!.Filter);
        Assert.Equal(2, rewritten.Expressions.Count);
        Assert.All(rewritten.Expressions, expression =>
        {
            var branch = Assert.IsType<SemanticAndFilter>(expression);
            Assert.Contains(c, branch.Expressions);
        });
    }

    [Fact]
    public void Rewrite_is_semantically_equivalent()
    {
        var a = new SemanticFieldFilter(new FieldId(1), SemanticFilterOperator.Eq, "A");
        var b = new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.Eq, "B");
        var c = new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "C");
        var filter = new SemanticAndFilter([new SemanticOrFilter([a, b]), c]);

        var plan = CreatePlan(filter);
        var rewritten = new PredicatePushdownRule().Apply(plan);

        var proof = SemanticEquivalenceProof.Create(plan, rewritten);
        Assert.True(proof.IsSatisfied);
    }

    [Fact]
    public void Rewrite_preserves_security_invariants()
    {
        var filter = new SemanticAndFilter([
            new SemanticOrFilter([
                new SemanticFieldFilter(new FieldId(1), SemanticFilterOperator.Eq, "A"),
                new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.Eq, "B")
            ]),
            new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "C")
        ]);

        var plan = CreatePlan(filter, ["tenant.isolation", "authorization.runtime"]);
        var rewritten = new PredicatePushdownRule().Apply(plan);

        var proof = SecurityPreservationProof.Create(plan, rewritten);
        Assert.True(proof.IsSatisfied);
    }

    [Fact]
    public void Does_not_expand_beyond_rule_budget()
    {
        var branches = Enumerable.Range(1, 17)
            .Select(i => (SemanticFilterExpression)new SemanticFieldFilter(
                new FieldId((ushort)i), SemanticFilterOperator.Eq, i))
            .ToArray();

        var filter = new SemanticAndFilter([
            new SemanticOrFilter(branches),
            new SemanticFieldFilter(new FieldId(100), SemanticFilterOperator.Eq, 100)
        ]);

        var plan = CreatePlan(filter);
        var rewritten = new PredicatePushdownRule().Apply(plan);

        Assert.Same(filter, rewritten.Root.QueryOptions!.Filter);
    }

    private static SemanticPlan CreatePlan(
        SemanticFilterExpression filter,
        IReadOnlyList<string>? invariants = null) =>
        new(
            new SemanticPlanNode(
                1,
                ExecutionOperation.Scan,
                new EntityId(1),
                [new FieldId(1), new FieldId(2), new FieldId(3)],
                null,
                null,
                [],
                new SemanticQueryOptions(filter),
                null),
            invariants);
}