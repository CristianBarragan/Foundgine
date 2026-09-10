using System.Security.Cryptography;
using System.Text;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.IR.Graph;

/// <summary>
/// Produces a deterministic identity for an immutable semantic operation graph.
/// This fingerprint represents semantic request shape and values; it contains
/// no provider or storage instructions.
/// </summary>
public static class SemanticOperationGraphFingerprint
{
    public static string Create(SemanticOperationGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var canonical = new StringBuilder("foundgine.semantic-operation-graph.v1\n");
        AppendNode(canonical, graph, graph.Root);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static void AppendNode(StringBuilder builder, SemanticOperationGraph graph, SemanticOperationGraphNode node)
    {
        Append(builder, "node", node.Id, node.EntityId.Value,
            node.ParentId?.ToString() ?? "-",
            node.ViaRelationship?.Value.ToString() ?? "-",
            node.ViaConnection?.Value.ToString() ?? "-");

        foreach (var field in node.Fields)
            Append(builder, "field", field.Value);
        foreach (var field in node.RequiredFields)
            Append(builder, "required", field.Value);

        AppendQuery(builder, node.QueryOptions);
        AppendAuthorization(builder, node.Authorization);

        foreach (var childId in node.Children)
            Append(builder, "edge", node.Id, childId);

        foreach (var childId in node.Children)
            AppendNode(builder, graph, graph.GetNode(childId));
    }

    private static void AppendQuery(StringBuilder builder, SemanticQueryOptions? options)
    {
        if (options is null)
        {
            Append(builder, "query", "-");
            return;
        }

        Append(builder, "limit", options.Limit?.ToString() ?? "-");
        Append(builder, "offset", options.Offset?.ToString() ?? "-");
        Append(builder, "after", options.After ?? "-");
        foreach (var order in options.EffectiveOrder)
            Append(builder, "order", order.Field.Value, (byte)order.Direction, (byte)order.Aggregate,
                string.Join('.', order.EffectivePath.Select(x => x.Value)));
        AppendFilter(builder, options.Filter);
    }

    private static void AppendFilter(StringBuilder builder, SemanticFilterExpression? filter)
    {
        switch (filter)
        {
            case null:
                Append(builder, "filter", "-");
                return;
            case SemanticFieldFilter field:
                Append(builder, "field-filter", field.Field.Value, (byte)field.Operator, ValueText(field.Value));
                return;
            case SemanticRelationshipFilter relationship:
                Append(builder, "relationship-filter", relationship.Relationship.Value, (byte)relationship.Quantifier);
                AppendFilter(builder, relationship.Predicate);
                return;
            case SemanticAggregateFilter aggregate:
                Append(builder, "aggregate-filter", aggregate.Relationship.Value, (byte)aggregate.Aggregate,
                    aggregate.Field?.Value.ToString() ?? "-", (byte)aggregate.Operator, ValueText(aggregate.Value));
                AppendFilter(builder, aggregate.Predicate);
                return;
            case SemanticAndFilter and:
                Append(builder, "and", and.Expressions.Count);
                foreach (var expression in and.Expressions) AppendFilter(builder, expression);
                return;
            case SemanticOrFilter or:
                Append(builder, "or", or.Expressions.Count);
                foreach (var expression in or.Expressions) AppendFilter(builder, expression);
                return;
            default: throw new NotSupportedException($"Unsupported semantic filter '{filter.GetType().Name}'.");
        }
    }

    private static void AppendAuthorization(StringBuilder builder,
        Foundgine.Core.Abstractions.AuthorizationPredicate? predicate)
    {
        if (predicate is null)
        {
            Append(builder, "auth", "-");
            return;
        }

        Append(builder, "auth", (byte)predicate.Kind, predicate.Name ?? "-", predicate.Value ?? "-");
        AppendAuthorization(builder, predicate.Left);
        AppendAuthorization(builder, predicate.Right);
    }

    private static string ValueText(object? value) =>
        global::Foundgine.Core.Semantic.SemanticValue.From(value).ToString();

    private static void Append(StringBuilder builder, string kind, params object[] values)
    {
        builder.Append(kind);
        foreach (var value in values)
        {
            var text = value?.ToString() ?? "";
            builder.Append('|').Append(text.Length).Append(':').Append(text);
        }

        builder.Append('\n');
    }
}