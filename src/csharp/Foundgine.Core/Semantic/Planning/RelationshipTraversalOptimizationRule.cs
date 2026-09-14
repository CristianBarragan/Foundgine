using Foundgine.Core.Semantic;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Adds a cardinality-aware traversal hint to relationship nodes.
/// One-to-one traversals are marked as SingleHop; collection traversals are
/// marked SetBased. The rule never changes topology, fields, filters,
/// authorization, pagination, or relationship identity. Providers may use the
/// hint to choose a cheaper physical traversal strategy when they can prove it
/// is safe.
/// </summary>
public sealed class RelationshipTraversalOptimizationRule : IPlanRewriteRule
{
    public string Name => "relationship.traversal.strategy";

    public IReadOnlyList<string> Preconditions =>
    [
        "plan contains relationship traversal nodes",
        "relationship cardinality metadata is available"
    ];

    public IReadOnlyList<string> SecurityObligations =>
    [
        "authorization.required",
        "visibility.relationship",
        "planning.cache-context-isolation"
    ];

    public double CostImpact => 0.5d;
    public double BenefitEstimate => 1.5d;
    public bool IsIdempotent => true;
    public int Priority => 20;

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
        var root = Rewrite(plan.Root, ref changed);
        return changed ? new SemanticPlan(root, plan.RequiredSecurityInvariants, plan.AuthorizationBinding) : plan;
    }

    private static SemanticPlanNode Rewrite(SemanticPlanNode node, ref bool changed)
    {
        var mode = node.TraversalMode;
        if (node.ViaRelationship is not null && node.RelationshipCardinality is { } cardinality)
        {
            var target = cardinality == RelationshipCardinality.One
                ? RelationshipTraversalMode.SingleHop
                : RelationshipTraversalMode.SetBased;
            if (mode != target)
            {
                mode = target;
                changed = true;
            }
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

        return changed
            ? node with { TraversalMode = mode, Children = children }
            : node;
    }

    private static bool ContainsEligible(SemanticPlanNode node) =>
        (node.ViaRelationship is not null && node.RelationshipCardinality is not null) ||
        node.Children.Any(ContainsEligible);
}
