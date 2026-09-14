using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Results;

namespace Foundgine.Core.Execution;

/// <summary>
/// Reconstructs the semantic result tree from flat provider rows. It uses
/// semantic identity fields to collapse repeated relational rows.
/// </summary>
public sealed class ResultMaterializer
{
    private readonly SemanticModel _model;

    public ResultMaterializer(SemanticModel model) =>
        _model = model ?? throw new ArgumentNullException(nameof(model));

    public SemanticResult Materialize(SemanticPlan plan, ExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);

        var roots = new List<SemanticResultNode>();
        foreach (var row in result.Rows)
            AddNode(plan.Root, row, roots, null);

        return new SemanticResult(
            roots,
            result.PageInfo is null
                ? null
                : new SemanticResultPageInfo(
                    result.PageInfo.StartCursor,
                    result.PageInfo.EndCursor,
                    result.PageInfo.HasNextPage,
                    result.PageInfo.HasPreviousPage),
            result.Evidence is null
                ? null
                : new SemanticResultEvidence(
                    result.Evidence.Provider,
                    result.Evidence.PlanFingerprint,
                    result.Evidence.AuthorizedNodeIds,
                    result.Evidence.RowsReturned,
                    result.Evidence.ElapsedMilliseconds,
                    result.Evidence.ProviderOperationFingerprint,
                    result.Evidence.IntentFingerprint,
                    result.Evidence.AuthorizationFingerprint));
    }

    private void AddNode(
        SemanticPlanNode planNode,
        ExecutionRow row,
        List<SemanticResultNode> siblings,
        SemanticResultNode? parent)
    {
        var entity = _model.Get(planNode.EntityId);
        var identityField = entity.Identity.FieldId;
        var identityValue = GetValue(row, planNode, identityField);

        if (identityValue is null)
        {
            if (parent is not null)
                return;

            throw new InvalidOperationException(
                $"Root entity '{entity.Name}' has a null identity value.");
        }

        var node = siblings.FirstOrDefault(x =>
            x.EntityId == planNode.EntityId && Equals(x.IdentityValue, identityValue));

        if (node is null)
        {
            var values = planNode.Fields.ToDictionary(
                field => field,
                field => GetValue(row, planNode, field));

            node = new SemanticResultNode(planNode.Id, planNode.EntityId, identityValue, values);
            siblings.Add(node);
        }

        foreach (var child in planNode.Children)
        {
            if (child.ViaRelationship is null)
                throw new InvalidOperationException(
                    $"Plan node {child.Id} has no relationship identity.");

            var relationship = entity.Relationships.FirstOrDefault(x => x.Id == child.ViaRelationship.Value);

            if (relationship is null)
                throw new InvalidOperationException(
                    $"Entity '{entity.Name}' has no relationship '{child.ViaRelationship.Value}'.");

            AddNode(
                child,
                row,
                node.GetChildren(relationship.Id),
                node);
        }
    }

    private static object? GetValue(
        ExecutionRow row,
        SemanticPlanNode planNode,
        FieldId fieldId)
    {
        var key = new ExecutionCellKey(planNode.Id, planNode.EntityId, fieldId);
        return row.EffectiveCells.TryGetValue(key, out var value) ? value : null;
    }
}