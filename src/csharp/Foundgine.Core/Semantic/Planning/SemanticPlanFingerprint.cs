using System.Globalization;
using System.Text;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Security;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Produces a deterministic key for an execution plan. The complete authorized
/// plan is represented, including authorization predicates and request values.
/// This intentionally keys exact plans rather than pretending that arbitrary
/// filter values can safely share a compiled provider plan.
/// </summary>
public static class SemanticPlanFingerprint
{
    public static string Create(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var builder = new StringBuilder(512);
        builder.Append("plan-v2|");
        AppendSecurityInvariants(builder,
            plan.RequiredSecurityInvariants != null ? plan.RequiredSecurityInvariants : new string[0]);
        AppendNode(builder, plan.Root, includePaginationValues: true);
        return builder.ToString();
    }

    /// <summary>
    /// Creates a cache key for the static query shape. Pagination values are
    /// deliberately excluded because the SQL provider binds LIMIT/OFFSET at
    /// execution time. Filters, ordering and authorization remain part of the
    /// key so unrelated query shapes do not share a provider plan.
    /// </summary>
    public static string CreateShapeKey(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var builder = new StringBuilder(512);
        builder.Append("plan-v2|");
        AppendSecurityInvariants(builder, plan.RequiredSecurityInvariants);
        AppendNode(builder, plan.Root, includePaginationValues: false);
        return builder.ToString();
    }

    private static void AppendSecurityInvariants(
        StringBuilder builder,
        IReadOnlyList<string>? invariants)
    {
        builder.Append("security[");

        if (invariants is not null)
        {
            foreach (var invariant in invariants.OrderBy(
                         x => x,
                         StringComparer.Ordinal))
            {
                builder.Append(invariant).Append(',');
            }
        }

        builder.Append(']');
    }

    private static void AppendNode(StringBuilder builder, SemanticPlanNode node, bool includePaginationValues)
    {
        builder.Append("node(")
            .Append(node.Id).Append('|')
            .Append((byte)node.Operation).Append('|')
            .Append(node.EntityId.Value).Append('|')
            .Append(node.ViaRelationship?.Value.ToString() ?? "-").Append('|')
            .Append(node.ViaConnection?.Value.ToString() ?? "-").Append('|')
            .Append(node.RelationshipCardinality?.ToString() ?? "-").Append('|')
            .Append((byte)node.TraversalMode).Append('|')
            .Append(node.TraversalOrder).Append('|')
            .Append((byte)node.AggregateExecutionStrategy).Append(')');

        builder.Append("fields[");
        foreach (var field in node.Fields)
            builder.Append(field.Value).Append(',');
        builder.Append(']');

        AppendQueryOptions(builder, node.QueryOptions, includePaginationValues);
        AppendPredicate(builder, node.Authorization);

        builder.Append("children[");
        foreach (var child in node.Children)
            AppendNode(builder, child, includePaginationValues);
        builder.Append(']');
    }

    private static void AppendQueryOptions(StringBuilder builder, SemanticQueryOptions? options,
        bool includePaginationValues)
    {
        if (options is null)
        {
            builder.Append("query[-]");
            return;
        }

        builder.Append("query[");
        if (includePaginationValues)
        {
            builder.Append(options.Limit?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('|')
                .Append(options.Offset?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('|');
            AppendValue(builder, options.After);
        }
        else
        {
            builder.Append("pagination-parameterized|");
        }

        builder.Append('|');

        builder.Append("order[");
        foreach (var term in options.EffectiveOrder)
        {
            builder.Append(term.Field.Value).Append(':')
                .Append((byte)term.Direction).Append(':')
                .Append((byte)term.Aggregate).Append(':');
            foreach (var relationship in term.EffectivePath)
                builder.Append(relationship.Value).Append('.');
            builder.Append(',');
        }

        builder.Append("]|");
        AppendFilter(builder, options.Filter);
        builder.Append(']');
    }

    private static void AppendFilter(StringBuilder builder, SemanticFilterExpression? filter)
    {
        switch (filter)
        {
            case null:
                builder.Append("filter[-]");
                break;
            case SemanticFieldFilter field:
                builder.Append("field(").Append(field.Field.Value).Append('|')
                    .Append((byte)field.Operator).Append('|');
                AppendValue(builder, field.Value);
                builder.Append(')');
                break;
            case SemanticRelationshipFilter relationship:
                builder.Append("relationship(").Append(relationship.Relationship.Value).Append('|')
                    .Append((byte)relationship.Quantifier).Append('|');
                AppendFilter(builder, relationship.Predicate);
                builder.Append(')');
                break;
            case SemanticAggregateFilter aggregate:
                builder.Append("aggregate(").Append(aggregate.Relationship.Value).Append('|')
                    .Append((byte)aggregate.Aggregate).Append('|')
                    .Append(aggregate.Field?.Value.ToString() ?? "-").Append('|')
                    .Append((byte)aggregate.Operator).Append('|');
                AppendValue(builder, aggregate.Value);
                builder.Append('|');
                AppendFilter(builder, aggregate.Predicate);
                builder.Append(')');
                break;
            case SemanticAndFilter and:
                builder.Append("and[");
                foreach (var expression in and.Expressions)
                    AppendFilter(builder, expression);
                builder.Append(']');
                break;
            case SemanticOrFilter or:
                builder.Append("or[");
                foreach (var expression in or.Expressions)
                    AppendFilter(builder, expression);
                builder.Append(']');
                break;
            default:
                throw new NotSupportedException($"Cannot fingerprint filter '{filter.GetType().Name}'.");
        }
    }

    private static void AppendPredicate(StringBuilder builder, AuthorizationPredicate? predicate)
    {
        builder.Append("auth[");
        if (predicate is not null)
            AppendPredicateNode(builder, predicate);
        builder.Append(']');
    }

    private static void AppendPredicateNode(StringBuilder builder, AuthorizationPredicate node)
    {
        builder.Append((byte)node.Kind).Append('(');
        AppendValue(builder, node.Name);
        builder.Append('|');
        AppendValue(builder, node.Value);
        builder.Append('|');
        if (node.Left is not null)
            AppendPredicateNode(builder, node.Left);
        builder.Append('|');
        if (node.Right is not null)
            AppendPredicateNode(builder, node.Right);
        builder.Append(')');
    }

    private static void AppendValue(StringBuilder builder, object? value)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        var type = value.GetType();
        builder.Append(type.AssemblyQualifiedName).Append('=');

        switch (value)
        {
            case string text:
                builder.Append(text.Length).Append(':').Append(text);
                break;
            case byte[] bytes:
                builder.Append(Convert.ToHexString(bytes));
                break;
            case System.Collections.IEnumerable sequence when value is not string:
                builder.Append('[');
                foreach (var item in sequence)
                {
                    AppendValue(builder, item);
                    builder.Append(',');
                }

                builder.Append(']');
                break;
            case IFormattable formattable:
                builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                builder.Append(value);
                break;
        }
    }
}