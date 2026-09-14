using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Adds a physical short-circuit hint for count predicates whose truth value
/// depends only on whether a collection is empty. The semantic filter remains
/// unchanged, so COUNT semantics, authorization, relationship visibility and
/// null/empty behavior stay defined by the semantic layer.
/// </summary>
public sealed class AggregateCardinalityOptimizationRule : IPlanRewriteRule
{
    public string Name => "aggregate.cardinality.short-circuit";

    public IReadOnlyList<string> Preconditions =>
    [
        "plan contains a COUNT aggregate filter",
        "COUNT aggregate has no target field",
        "count comparison can be reduced to an emptiness test",
        "all eligible COUNT aggregates on the node require the same strategy"
    ];

    public IReadOnlyList<string> SecurityObligations =>
    [
        "authorization.required",
        "authorization.runtime",
        "visibility.relationship",
        "planning.cache-context-isolation"
    ];

    public double CostImpact => 0.5d;
    public double BenefitEstimate => 3.0d;
    public bool IsIdempotent => true;
    public int Priority => 40;

    public bool CanApply(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return RequiresRewrite(plan.Root);
    }

    public SemanticPlan Apply(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!CanApply(plan))
            return plan;

        var changed = false;
        var root = Rewrite(plan.Root, ref changed);
        return changed ? new SemanticPlan(root, plan.RequiredSecurityInvariants, plan.AuthorizationBinding) : plan;
    }

    private static SemanticPlanNode Rewrite(SemanticPlanNode node, ref bool changed)
    {
        // Recompute the hint from the current semantic filter on every pass.
        // A previous rewrite may have changed the filter shape (for example by
        // introducing an aggregate predicate), which invalidates an earlier hint.
        // Never retain a stale physical strategy merely because no new candidate
        // can be derived.
        var candidate = GetStrategy(node.QueryOptions?.Filter)
                        ?? AggregateExecutionStrategy.Default;
        var strategy = candidate;
        if (strategy != node.AggregateExecutionStrategy)
        {
            changed = true;
        }

        var children = new SemanticPlanNode[node.Children.Count];
        var childrenChanged = false;
        for (var i = 0; i < node.Children.Count; i++)
        {
            children[i] = Rewrite(node.Children[i], ref changed);
            childrenChanged |= !ReferenceEquals(children[i], node.Children[i]);
        }

        if (childrenChanged)
            changed = true;

        if (strategy != node.AggregateExecutionStrategy || childrenChanged)
            return node with { AggregateExecutionStrategy = strategy, Children = children };

        return node;
    }

    private static bool RequiresRewrite(SemanticPlanNode node)
    {
        var desired = GetStrategy(node.QueryOptions?.Filter)
                      ?? AggregateExecutionStrategy.Default;

        return desired != node.AggregateExecutionStrategy || node.Children.Any(RequiresRewrite);
    }

    private static AggregateExecutionStrategy? GetStrategy(SemanticFilterExpression? filter)
    {
        var aggregates = new List<SemanticAggregateFilter>();
        CollectAggregates(filter, aggregates);
        if (aggregates.Count == 0 || aggregates.Any(a =>
                a.Aggregate != SemanticFilterAggregate.Count || a.Field is not null || a.Predicate is not null))
            return null;

        AggregateExecutionStrategy? result = null;
        foreach (var aggregate in aggregates)
        {
            var strategy = GetCountStrategy(aggregate.Operator, aggregate.Value);
            if (strategy is null)
                return null;
            if (result is null)
                result = strategy;
            else if (result.Value != strategy.Value)
                return null;
        }

        return result;
    }

    private static void CollectAggregates(SemanticFilterExpression? filter, ICollection<SemanticAggregateFilter> result)
    {
        switch (filter)
        {
            case SemanticAggregateFilter aggregate:
                result.Add(aggregate);
                break;
            case SemanticAndFilter and:
                foreach (var child in and.Expressions) CollectAggregates(child, result);
                break;
            case SemanticOrFilter or:
                foreach (var child in or.Expressions) CollectAggregates(child, result);
                break;
            // Relationship predicates execute in a different semantic scope.
            // Their aggregate filters must be optimized by the target relationship
            // context, not by the current node-level strategy hint.
            case SemanticRelationshipFilter:
                break;
        }
    }

    private static AggregateExecutionStrategy? GetCountStrategy(
        SemanticAggregateFilterOperator op,
        object? value) =>
        AggregateExecutionStrategyResolver.Resolve(op, value);
}