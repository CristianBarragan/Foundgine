using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class ProjectionPruningRuleTests
{
    [Fact]
    public void Removes_duplicate_projection_fields_without_reordering_output()
    {
        var plan = CreatePlan([new FieldId(1), new FieldId(2), new FieldId(1), new FieldId(3)]);

        var rewritten = new ProjectionPruningRule().Apply(plan);

        Assert.Equal([new FieldId(1), new FieldId(2), new FieldId(3)], rewritten.Root.Fields);
    }

    [Fact]
    public void Retains_fields_required_by_filter()
    {
        var filter = new SemanticFieldFilter(new FieldId(7), SemanticFilterOperator.Eq, "active");
        var plan = CreatePlan([new FieldId(1), new FieldId(1)], new SemanticQueryOptions(filter));

        var rewritten = new ProjectionPruningRule().Apply(plan);

        Assert.Contains(new FieldId(7), ProjectionPruningRequirements.RequiredRootFields(rewritten.Root));
        Assert.Contains(new FieldId(1), rewritten.Root.Fields);
    }

    [Fact]
    public void Retains_fields_required_by_ordering()
    {
        var order = new SemanticOrderTerm(new FieldId(9), SemanticSortDirection.Desc);
        var plan = CreatePlan([new FieldId(1), new FieldId(1)], new SemanticQueryOptions(Order: [order]));

        var rewritten = new ProjectionPruningRule().Apply(plan);

        Assert.Contains(new FieldId(9), ProjectionPruningRequirements.RequiredRootFields(rewritten.Root));
    }

    [Fact]
    public void Rewrite_is_semantically_equivalent()
    {
        var plan = CreatePlan([new FieldId(1), new FieldId(1)]);
        var rewritten = new ProjectionPruningRule().Apply(plan);

        var proof = SemanticEquivalenceProof.Create(plan, rewritten);
        Assert.True(proof.IsSatisfied);
    }

    [Fact]
    public void Rewrite_preserves_security_invariants()
    {
        var plan = CreatePlan([new FieldId(1), new FieldId(1)], null,
            ["tenant.isolation", "authorization.runtime", "visibility.field"]);
        var rewritten = new ProjectionPruningRule().Apply(plan);

        var proof = SecurityPreservationProof.Create(plan, rewritten);
        Assert.True(proof.IsSatisfied);
    }

    [Fact]
    public void Unique_requested_fields_are_not_pruned()
    {
        var plan = CreatePlan([new FieldId(1), new FieldId(2)]);

        var rewritten = new ProjectionPruningRule().Apply(plan);

        Assert.Same(plan, rewritten);
    }

    private static SemanticPlan CreatePlan(
        IReadOnlyList<FieldId> fields,
        SemanticQueryOptions? options = null,
        IReadOnlyList<string>? invariants = null) =>
        new(
            new SemanticPlanNode(
                1,
                ExecutionOperation.Scan,
                new EntityId(1),
                fields,
                null,
                null,
                [],
                options),
            invariants);
}
