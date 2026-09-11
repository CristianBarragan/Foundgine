using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Removes redundant duplicate projection fields while preserving every field
/// required by the result contract and semantic query evaluation.
///
/// The current SemanticPlan represents requested output and execution fields
/// in one collection. Consequently this rule intentionally does not remove a
/// unique requested field merely because it is not referenced by a predicate.
/// Full dead-field pruning requires a separate requested-vs-working projection
/// representation and is therefore outside this milestone.
/// </summary>
public sealed class ProjectionPruningRule : IPlanRewriteRule
{
    public string Name => "projection.pruning";

    public IReadOnlyList<string> Preconditions =>
        ["plan contains a projection", "projection contains redundant fields"];

    public IReadOnlyList<string> SecurityObligations =>
        ["visibility.field", "visibility.relationship", "authorization.required"];

    public double CostImpact => 0.25d;

    public double BenefitEstimate => 1.5d;

    public bool IsIdempotent => true;

    public int Priority => 20;

    public bool CanApply(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ContainsRedundantProjection(plan.Root);
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
        var required = ProjectionPruningRequirements.RequiredRootFields(node);
        var fields = DeduplicatePreservingOrder(node.Fields, required, out var nodeChanged);
        if (nodeChanged)
            changed = true;

        var children = new SemanticPlanNode[node.Children.Count];
        for (var i = 0; i < node.Children.Count; i++)
            children[i] = RewriteNode(node.Children[i], ref changed);

        if (ChildrenChanged(node.Children, children))
            changed = true;

        if (changed && (nodeChanged || !children.SequenceEqual(node.Children)))
            return node with { Fields = fields, Children = children };

        return node;
    }

    private static IReadOnlyList<FieldId> DeduplicatePreservingOrder(
        IReadOnlyList<FieldId> fields,
        IReadOnlySet<FieldId> required,
        out bool changed)
    {
        changed = false;
        if (fields.Count < 2)
            return fields;

        var seen = new HashSet<FieldId>();
        var result = new List<FieldId>(fields.Count);
        foreach (var field in fields)
        {
            // Required is deliberately consulted before removing anything. A
            // required field can only lose redundant occurrences, never its
            // first occurrence.
            if (required.Contains(field) && seen.Add(field))
            {
                result.Add(field);
                continue;
            }

            if (seen.Add(field))
                result.Add(field);
            else
                changed = true;
        }

        return changed ? result : fields;
    }

    private static bool ContainsRedundantProjection(SemanticPlanNode node) =>
        HasDuplicateFields(node.Fields) || node.Children.Any(ContainsRedundantProjection);

    private static bool ChildrenChanged(IReadOnlyList<SemanticPlanNode> before, IReadOnlyList<SemanticPlanNode> after)
    {
        if (before.Count != after.Count)
            return true;

        for (var i = 0; i < before.Count; i++)
            if (!ReferenceEquals(before[i], after[i]))
                return true;

        return false;
    }

    private static bool HasDuplicateFields(IReadOnlyList<FieldId> fields)
    {
        if (fields.Count < 2)
            return false;

        var seen = new HashSet<FieldId>();
        foreach (var field in fields)
            if (!seen.Add(field))
                return true;

        return false;
    }
}