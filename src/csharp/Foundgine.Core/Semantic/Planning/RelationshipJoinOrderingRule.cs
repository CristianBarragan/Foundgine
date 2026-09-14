using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Assigns a deterministic physical traversal order to sibling relationship
/// nodes. The logical child collection is not reordered, so result shaping and
/// requested field order remain unchanged. Providers may use the order when
/// constructing an execution strategy, subject to their own semantic and
/// security conformance checks.
/// </summary>
public sealed class RelationshipJoinOrderingRule : IPlanRewriteRule
{
    public string Name => "relationship.join.order";

    public IReadOnlyList<string> Preconditions =>
    [
        "plan contains two or more sibling relationship traversals",
        "relationship cardinality is known for each candidate traversal",
        "no explicit sibling execution order is already fixed"
    ];

    public IReadOnlyList<string> SecurityObligations =>
    [
        "authorization.required",
        "authorization.runtime",
        "visibility.relationship",
        "planning.cache-context-isolation"
    ];

    public double CostImpact => 1.0d;
    public double BenefitEstimate => 2.5d;
    public bool IsIdempotent => true;
    public int Priority => 30;

    public bool CanApply(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ContainsEligibleSiblingSet(plan.Root);
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
        var children = node.Children.ToArray();
        var childChanged = false;

        var candidates = children
            .Select((child, index) => (child, index))
            .Where(x => x.child.ViaRelationship is not null &&
                        x.child.RelationshipCardinality is not null)
            .ToArray();

        if (candidates.Length >= 2 && candidates.All(x => x.child.TraversalOrder < 0))
        {
            var ordered = candidates
                .OrderBy(x => SelectivityClass(x.child))
                .ThenBy(x => x.child.RelationshipCardinality == RelationshipCardinality.One ? 0 : 1)
                .ThenBy(x => x.child.ViaRelationship!.Value.Value)
                .ThenBy(x => x.index)
                .ToArray();

            for (var rank = 0; rank < ordered.Length; rank++)
            {
                var (child, index) = ordered[rank];
                var updated = child with { TraversalOrder = rank };
                if (!ReferenceEquals(updated, child))
                {
                    children[index] = updated;
                    childChanged = true;
                }
            }
        }

        for (var i = 0; i < children.Length; i++)
        {
            var updated = Rewrite(children[i], ref changed);
            if (!ReferenceEquals(updated, children[i]))
            {
                children[i] = updated;
                childChanged = true;
            }
        }

        if (childChanged)
        {
            changed = true;
            return node with { Children = children };
        }

        return node;
    }

    private static int SelectivityClass(SemanticPlanNode node)
    {
        var score = 0;
        if (node.QueryOptions?.Filter is not null)
            score -= 3;
        if (node.QueryOptions?.Filter is SemanticRelationshipFilter or SemanticAggregateFilter)
            score -= 1;
        if (node.QueryOptions?.Limit is > 0)
            score -= 2;
        if (node.RelationshipCardinality == RelationshipCardinality.One)
            score -= 1;
        return score;
    }

    private static bool ContainsEligibleSiblingSet(SemanticPlanNode node)
    {
        var count = node.Children.Count(child =>
            child.ViaRelationship is not null &&
            child.RelationshipCardinality is not null &&
            child.TraversalOrder < 0);

        return count >= 2 || node.Children.Any(ContainsEligibleSiblingSet);
    }
}
