using Foundgine.Core.Semantic;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Provider-neutral description of an authorized plan suitable for inspection
/// before execution. This is deliberately derived from the canonical plan;
/// it is not a second planning representation.
/// </summary>
public sealed record PlanInspection(
    SemanticPlan Plan,
    string PlanFingerprint,
    IReadOnlyList<PlanInspectionNode> Nodes,
    PlanEffectSummary Effects);

public sealed record PlanInspectionNode(
    int NodeId,
    string Operation,
    ulong EntityId,
    IReadOnlyList<ulong> FieldIds,
    ulong? ViaRelationshipId,
    ulong? ViaConnectionId,
    bool AuthorizationApplied,
    IReadOnlyList<PlanInspectionNode> Children);

/// <summary>
/// Conservative effect summary. It reports what the semantic plan declares;
/// it does not claim provider-side effects that the plan cannot establish.
/// </summary>
public sealed record PlanEffectSummary(
    bool HasWrites,
    bool HasExternalSideEffects,
    int AffectedPlanNodes,
    IReadOnlyList<string> Effects);

public static class PlanInspector
{
    public static PlanInspection Inspect(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var nodes = Flatten(plan.Root).ToArray();
        var effects = Array.Empty<string>();

        return new PlanInspection(
            plan,
            SemanticPlanFingerprint.Create(plan),
            nodes.Select(ToInspection).ToArray(),
            new PlanEffectSummary(
                HasWrites: false,
                HasExternalSideEffects: false,
                AffectedPlanNodes: nodes.Length,
                Effects: effects));
    }

    private static IEnumerable<SemanticPlanNode> Flatten(SemanticPlanNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }

    private static PlanInspectionNode ToInspection(SemanticPlanNode node) =>
        new(
            node.Id,
            node.Operation.ToString(),
            node.EntityId.Value,
            node.Fields.Select(x => x.Value).ToArray(),
            node.ViaRelationship?.Value,
            node.ViaConnection?.Value,
            node.Authorization is not null,
            node.Children.Select(ToInspection).ToArray());
}