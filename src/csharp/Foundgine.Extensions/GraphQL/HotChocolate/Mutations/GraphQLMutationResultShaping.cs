using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution.Mutation;

namespace Foundgine.Extensions.GraphQL.HotChocolate;

public sealed record GraphQLMutationResultField(
    FieldId Field,
    string GraphQLName,
    string Alias)
{
    public string ResponseName => Alias;

    public GraphQLMutationResultField(FieldId field, string responseName)
        : this(field, responseName, responseName)
    {
    }
}

public sealed record GraphQLMutationResultShape(
    IReadOnlyList<GraphQLMutationResultField> Fields,
    IReadOnlyList<GraphQLMutationResultRelationship> Relationships);

public sealed record GraphQLMutationResultRelationship(
    RelationshipId Relationship,
    string GraphQLName,
    string Alias,
    GraphQLMutationResultShape Shape,
    bool IsCollection)
{
    public string ResponseName => Alias;
}

public sealed record GraphQLMutationAdaptation(
    Foundgine.Core.Semantic.Planning.Mutation.NestedMutationIntent Intent,
    GraphQLMutationResultShape ResultShape)
{
    public GraphQLMutationResultShape Result => ResultShape;
}

/// <summary>
/// One entry of a batched GraphQL mutation document (see
/// <c>HotChocolateMutationAdapter.AdaptBatchWithResultShape</c>). ResultKey is the field's
/// GraphQL alias (or, for the single-unaliased-field convenience case, its field name) -
/// use it both to key the response object per-item and to line up each item's planned
/// operations with <c>MutationPlanner.Plan(IReadOnlyList&lt;NestedMutationIntent&gt;)</c>.
/// </summary>
public sealed record GraphQLMutationBatchItem(
    string ResultKey,
    GraphQLMutationAdaptation Adaptation);

public static class GraphQLMutationResultShaper
{
    public static Dictionary<string, object?> Shape(
        MutationMaterializedNode node,
        GraphQLMutationResultShape shape)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(shape);

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in shape.Fields)
            result[field.ResponseName] = node.Values.TryGetValue(field.Field, out var value) ? value : null;

        foreach (var relationship in shape.Relationships)
        {
            node.Children.TryGetValue(relationship.Relationship, out var children);
            children ??= [];

            var materializedChildren = children
                .Where(child => child.Values.Count != 0)
                .ToArray();

            if (relationship.IsCollection)
            {
                result[relationship.ResponseName] = materializedChildren
                    .Select(child => Shape(child, relationship.Shape))
                    .Cast<object?>()
                    .ToArray();
            }
            else
            {
                result[relationship.ResponseName] = materializedChildren.Length == 0
                    ? null
                    : Shape(materializedChildren[0], relationship.Shape);
            }
        }

        return result;
    }

    public static Dictionary<string, object?>? ShapeRoot(
        MutationMaterializedResult materialized,
        GraphQLMutationResultShape shape)
    {
        ArgumentNullException.ThrowIfNull(materialized);
        ArgumentNullException.ThrowIfNull(shape);

        var root = materialized.Roots.FirstOrDefault();
        if (root is null || root.Values.Count == 0)
            return null;

        return Shape(root, shape);
    }
}