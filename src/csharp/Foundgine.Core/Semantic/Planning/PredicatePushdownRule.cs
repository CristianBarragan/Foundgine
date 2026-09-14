using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Pushes a conjunctive predicate into an OR predicate using the distributive
/// law: (A OR B) AND C becomes (A AND C) OR (B AND C).
///
/// This is a provider-neutral logical pushdown. It does not move predicates
/// across relationship boundaries, authorization boundaries, pagination, or
/// cardinality-changing operations. Those transformations require richer
/// relationship and cardinality contracts.
/// </summary>
public sealed class PredicatePushdownRule : IPlanRewriteRule
{
    private const int MaxExpansionTerms = 16;

    public string Name => "predicate.pushdown.disjunction";

    public IReadOnlyList<string> Preconditions =>
        ["plan contains a query filter", "filter contains AND with OR operand", "rewrite expansion remains bounded"];

    public IReadOnlyList<string> SecurityObligations =>
        ["authorization.required", "visibility.field", "visibility.relationship"];

    public double CostImpact => 1d;

    public double BenefitEstimate => 2d;

    public bool IsIdempotent => true;

    public int Priority => 10;

    public bool CanApply(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ContainsPushableFilter(plan.Root);
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
        if (options?.Filter is not null)
        {
            var rewritten = PushOnce(options.Filter);
            if (!ReferenceEquals(rewritten, options.Filter))
            {
                options = options with { Filter = rewritten };
                changed = true;
            }
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

        if (changed && (options is not null || childrenChanged))
            return node with { QueryOptions = options, Children = children };

        return node;
    }

    private static SemanticFilterExpression PushOnce(SemanticFilterExpression filter)
    {
        switch (filter)
        {
            case SemanticAndFilter semanticAnd:
                var expressions = semanticAnd.Expressions.ToList();
                var orIndex = expressions.FindIndex(x => x is SemanticOrFilter);
                if (orIndex < 0)
                    return filter;

                var or = (SemanticOrFilter)expressions[orIndex];
                var other = expressions.Where((_, index) => index != orIndex).ToArray();
                var termCount = or.Expressions.Count;
                if (termCount == 0 || termCount * Math.Max(1, other.Length) > MaxExpansionTerms)
                    return filter;

                var distributed = or.Expressions
                    .Select(branch =>
                    {
                        var terms = new List<SemanticFilterExpression> { branch };
                        terms.AddRange(other);
                        return terms.Count == 1
                            ? terms[0]
                            : new SemanticAndFilter(terms);
                    })
                    .ToArray();

                return new SemanticOrFilter(distributed);

            case SemanticOrFilter semanticOr:
                return new SemanticOrFilter(
                    semanticOr.Expressions.Select(PushOnce).ToArray());

            case SemanticRelationshipFilter relationship:
                var relationshipPredicate = PushOnce(relationship.Predicate);
                return ReferenceEquals(relationshipPredicate, relationship.Predicate)
                    ? relationship
                    : relationship with { Predicate = relationshipPredicate };

            default:
                return filter;
        }
    }

    private static bool ContainsPushableFilter(SemanticPlanNode node)
    {
        if (node.QueryOptions?.Filter is not null && ContainsPushable(node.QueryOptions.Filter))
            return true;

        return node.Children.Any(ContainsPushableFilter);
    }

    private static bool ContainsPushable(SemanticFilterExpression filter)
    {
        return filter switch
        {
            SemanticAndFilter and =>
                and.Expressions.Any(x => x is SemanticOrFilter) ||
                and.Expressions.Any(ContainsPushable),
            SemanticOrFilter or => or.Expressions.Any(ContainsPushable),
            SemanticRelationshipFilter relationship => ContainsPushable(relationship.Predicate),
            _ => false
        };
    }
}