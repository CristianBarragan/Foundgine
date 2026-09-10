using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Pushes an existential relationship predicate into a COUNT aggregate.
///
/// The transformation is only applied to the proven-equivalent shape:
/// COUNT(R) > 0 (or >= 1 / != 0) AND SOME(R, P)
/// becomes COUNT(R WHERE P) > 0.
///
/// This keeps the semantic meaning intact while allowing providers to evaluate
/// the child predicate inside the aggregate subquery instead of maintaining a
/// separate relationship-existence check.
/// </summary>
public sealed class AggregateRelationshipFilterPushdownRule : IPlanRewriteRule
{
    public string Name => "aggregate.relationship-filter.pushdown";

    public IReadOnlyList<string> Preconditions =>
    [
        "plan contains an AND filter",
        "AND contains an eligible COUNT existence predicate",
        "AND contains a SOME relationship predicate for the same relationship",
        "COUNT aggregate does not already contain a predicate"
    ];

    public IReadOnlyList<string> SecurityObligations =>
    [
        "authorization.required",
        "authorization.runtime",
        "visibility.relationship",
        "planning.cache-context-isolation"
    ];

    public double CostImpact => 1.25d;
    public double BenefitEstimate => 4.0d;
    public bool IsIdempotent => true;
    public int Priority => 35;

    public IReadOnlyList<string> MustRunBefore =>
    [
        "aggregate.cardinality.short-circuit"
    ];

    public bool CanApply(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ContainsEligible(plan.Root);
    }

    public SemanticPlan Apply(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!CanApply(plan))
            return plan;

        var changed = false;
        var root = RewriteNode(plan.Root, ref changed);
        return changed ? new SemanticPlan(root, plan.RequiredSecurityInvariants, plan.AuthorizationBinding) : plan;
    }

    private static SemanticPlanNode RewriteNode(SemanticPlanNode node, ref bool changed)
    {
        var options = node.QueryOptions;
        var filterChanged = false;
        if (options?.Filter is not null)
        {
            var originalFilter = options.Filter;
            var rewritten = RewriteFilter(originalFilter, ref changed);
            filterChanged = !ReferenceEquals(rewritten, originalFilter);
            if (filterChanged)
                options = options with { Filter = rewritten };
        }

        var children = new SemanticPlanNode[node.Children.Count];
        var childrenChanged = false;
        for (var i = 0; i < node.Children.Count; i++)
        {
            children[i] = RewriteNode(node.Children[i], ref changed);
            childrenChanged |= !ReferenceEquals(children[i], node.Children[i]);
        }

        if (childrenChanged)
            changed = true;

        if (filterChanged)
            changed = true;

        return changed && (options is not null || childrenChanged)
            ? node with
            {
                QueryOptions = options,
                Children = children,
                // The pushdown changes the aggregate's semantic shape. Any
                // previously attached cardinality hint was derived from the
                // old bare COUNT and is therefore no longer valid. The
                // cardinality rule may re-derive a hint later if the new shape
                // proves eligible.
                AggregateExecutionStrategy = filterChanged
                    ? AggregateExecutionStrategy.Default
                    : node.AggregateExecutionStrategy
            }
            : node;
    }

    private static SemanticFilterExpression RewriteFilter(
        SemanticFilterExpression filter,
        ref bool changed)
    {
        switch (filter)
        {
            case SemanticAndFilter and:
            {
                var expressions = and.Expressions.ToArray();

                for (var i = 0; i < expressions.Length; i++)
                {
                    if (expressions[i] is not SemanticAggregateFilter aggregate ||
                        !IsEligibleCountExists(aggregate) ||
                        aggregate.Predicate is not null)
                    {
                        continue;
                    }

                    for (var j = 0; j < expressions.Length; j++)
                    {
                        if (i == j ||
                            expressions[j] is not SemanticRelationshipFilter relationship ||
                            relationship.Quantifier != SemanticRelationshipQuantifier.Some ||
                            relationship.Relationship != aggregate.Relationship)
                        {
                            continue;
                        }

                        var rewrittenAggregate = aggregate with
                        {
                            Predicate = relationship.Predicate
                        };

                        var remaining = new List<SemanticFilterExpression>(
                            expressions.Length - 1);

                        for (var k = 0; k < expressions.Length; k++)
                        {
                            if (k == i)
                            {
                                remaining.Add(rewrittenAggregate);
                            }
                            else if (k != j)
                            {
                                remaining.Add(expressions[k]);
                            }
                        }

                        changed = true;

                        return remaining.Count switch
                        {
                            0 => throw new InvalidOperationException(
                                "Aggregate pushdown produced an empty AND expression."),

                            1 => remaining[0],

                            _ => new SemanticAndFilter(remaining)
                        };
                    }
                }

                var nested = new SemanticFilterExpression[expressions.Length];
                var nodeChanged = false;

                for (var i = 0; i < expressions.Length; i++)
                {
                    var rewritten = RewriteFilter(
                        expressions[i],
                        ref changed);

                    nested[i] = rewritten;

                    if (!ReferenceEquals(rewritten, expressions[i]))
                        nodeChanged = true;
                }

                if (!nodeChanged)
                    return filter;

                changed = true;
                return new SemanticAndFilter(nested);
            }

            case SemanticOrFilter or:
            {
                var expressions = or.Expressions.ToArray();
                var nested = new SemanticFilterExpression[expressions.Length];
                var nodeChanged = false;

                for (var i = 0; i < expressions.Length; i++)
                {
                    var rewritten = RewriteFilter(
                        expressions[i],
                        ref changed);

                    nested[i] = rewritten;

                    if (!ReferenceEquals(rewritten, expressions[i]))
                        nodeChanged = true;
                }

                if (!nodeChanged)
                    return filter;

                changed = true;
                return new SemanticOrFilter(nested);
            }

            case SemanticRelationshipFilter relationship:
            {
                var predicate = RewriteFilter(
                    relationship.Predicate,
                    ref changed);

                if (ReferenceEquals(predicate, relationship.Predicate))
                    return filter;

                return relationship with
                {
                    Predicate = predicate
                };
            }

            default:
                return filter;
        }
    }

    private static bool ContainsEligible(SemanticPlanNode node)
    {
        if (node.QueryOptions?.Filter is not null && ContainsEligible(node.QueryOptions.Filter))
            return true;
        return node.Children.Any(ContainsEligible);
    }

    private static bool ContainsEligible(SemanticFilterExpression filter) =>
        filter switch
        {
            SemanticAndFilter and =>
                and.Expressions.OfType<SemanticAggregateFilter>().Any(IsEligibleCountExists) &&
                and.Expressions.OfType<SemanticRelationshipFilter>()
                    .Any(r => r.Quantifier == SemanticRelationshipQuantifier.Some),
            SemanticRelationshipFilter relationship => ContainsEligible(relationship.Predicate),
            SemanticOrFilter or => or.Expressions.Any(ContainsEligible),
            _ => false
        };

    private static bool IsEligibleCountExists(SemanticAggregateFilter aggregate)
    {
        if (aggregate.Aggregate != SemanticFilterAggregate.Count || aggregate.Field is not null ||
            aggregate.Predicate is not null)
            return false;

        return AggregateExecutionStrategyResolver.Resolve(aggregate.Operator, aggregate.Value) ==
               AggregateExecutionStrategy.CountExistsShortCircuit;
    }
}