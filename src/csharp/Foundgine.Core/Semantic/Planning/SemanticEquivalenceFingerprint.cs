using System.Globalization;
using System.Text;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Produces a canonical semantic identity used to determine whether two plans
/// have the same provider-neutral meaning. Unlike the execution fingerprint,
/// this representation normalizes only transformations that Foundgine defines
/// as semantically equivalent.
/// </summary>
public static class SemanticEquivalenceFingerprint
{
    public static string Create(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var builder = new StringBuilder(512);
        builder.Append("semantic-v1|");
        AppendSecurity(builder, plan.EffectiveSecurityInvariants);
        AppendNode(builder, plan.Root);
        return builder.ToString();
    }

    private static void AppendSecurity(StringBuilder builder, IReadOnlyList<string> invariants)
    {
        builder.Append("security[");
        foreach (var invariant in invariants.OrderBy(x => x, StringComparer.Ordinal))
            builder.Append(invariant).Append(',');
        builder.Append("]|");
    }

    private static void AppendNode(StringBuilder builder, SemanticPlanNode node)
    {
        builder.Append("node(").Append(node.Id).Append('|')
            .Append((byte)node.Operation).Append('|')
            .Append(node.EntityId.Value).Append('|')
            .Append(node.ViaRelationship?.Value.ToString() ?? "-").Append('|')
            .Append(node.ViaConnection?.Value.ToString() ?? "-").Append(')');

        builder.Append("fields[");
        var seenFields = new HashSet<ulong>();
        foreach (var field in node.Fields)
        {
            if (seenFields.Add(field.Value))
                builder.Append(field.Value).Append(',');
        }

        builder.Append("]|");

        AppendQueryOptions(builder, node.QueryOptions);
        builder.Append("auth[");
        AppendAuthorization(builder, node.Authorization);
        builder.Append("]|");

        builder.Append("children[");
        foreach (var child in node.Children)
            AppendNode(builder, child);
        builder.Append(']');
    }

    private static void AppendQueryOptions(StringBuilder builder, SemanticQueryOptions? options)
    {
        if (options is null)
        {
            builder.Append("query[-]|");
            return;
        }

        builder.Append("query[")
            .Append(options.Limit?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('|')
            .Append(options.Offset?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('|');
        AppendValue(builder, options.After);
        builder.Append("|order[");
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
        builder.Append("]|");
    }

    private static void AppendFilter(StringBuilder builder, SemanticFilterExpression? filter)
    {
        if (filter is not null)
            filter = CanonicalizeAggregateRelationshipPushdown(filter);

        switch (filter)
        {
            case null:
                builder.Append("-");
                return;
            case SemanticFieldFilter field:
                builder.Append("field(").Append(field.Field.Value).Append('|')
                    .Append((byte)field.Operator).Append('|');
                AppendValue(builder, field.Value);
                builder.Append(')');
                return;
            case SemanticRelationshipFilter relationship:
                builder.Append("relationship(").Append(relationship.Relationship.Value).Append('|')
                    .Append((byte)relationship.Quantifier).Append('|');
                AppendFilter(builder, relationship.Predicate);
                builder.Append(')');
                return;
            case SemanticAggregateFilter aggregate:
                builder.Append("aggregate(").Append(aggregate.Relationship.Value).Append('|')
                    .Append((byte)aggregate.Aggregate).Append('|')
                    .Append(aggregate.Field?.Value.ToString() ?? "-").Append('|')
                    .Append((byte)aggregate.Operator).Append('|');
                AppendValue(builder, aggregate.Value);
                builder.Append('|');
                AppendFilter(builder, aggregate.Predicate);
                builder.Append(')');
                return;
            case SemanticAndFilter and:
                AppendCanonicalBooleanFilter(builder, and);
                return;
            case SemanticOrFilter or:
                AppendCanonicalBooleanFilter(builder, or);
                return;
            default:
                throw new NotSupportedException(
                    $"Cannot establish semantic equivalence for filter '{filter.GetType().Name}'.");
        }
    }

    private static void AppendCanonicalBooleanFilter(StringBuilder builder, SemanticFilterExpression filter)
    {
        // AggregateRelationshipFilterPushdownRule replaces:
        //   COUNT(R) > 0 AND SOME(R, P)
        // with:
        //   COUNT(R WHERE P) > 0
        // This is a semantic normalization, not an execution-plan identity. Keep it
        // here (rather than SemanticPlanFingerprint) so the rewrite composer can prove
        // the transformation without weakening exact plan/cache fingerprints.
        filter = CanonicalizeAggregateRelationshipPushdown(filter);

        const int maxTerms = 32;
        var terms = ToDnf(filter, maxTerms);
        if (terms is null)
        {
            switch (filter)
            {
                case SemanticAndFilter and:
                    AppendCommutativeFilter(builder, "and", and.Expressions);
                    return;
                case SemanticOrFilter or:
                    AppendCommutativeFilter(builder, "or", or.Expressions);
                    return;
                default:
                    throw new InvalidOperationException("Expected a boolean filter.");
            }
        }

        builder.Append("dnf[");
        foreach (var term in terms
                     .Select(t => t.OrderBy(CreateFilterKey, StringComparer.Ordinal).Select(CreateFilterKey).ToArray())
                     .OrderBy(t => string.Join("&", t), StringComparer.Ordinal))
        {
            builder.Append('(');
            foreach (var atom in term)
                builder.Append(atom).Append(',');
            builder.Append(')');
        }

        builder.Append(']');
    }

    private static SemanticFilterExpression CanonicalizeAggregateRelationshipPushdown(
        SemanticFilterExpression filter)
    {
        // Canonicalize the logical equivalence in the correct order. For an AND
        // containing COUNT(R) > 0 and SOME(R, P), the pair must be recognized
        // before the COUNT expression is individually normalized to SOME(R, null).
        // Otherwise the two operands become indistinguishable relationship filters
        // and the semantic fingerprint cannot prove the pushdown.
        switch (filter)
        {
            case SemanticAndFilter and:
            {
                var expressions = and.Expressions
                    .Select(CanonicalizeAggregateRelationshipPushdown)
                    .ToList();

                // The recursive normalization above may already have converted a
                // filtered COUNT into SOME. Therefore perform the equivalence in a
                // second canonical pass over the original AND shape when possible.
                var raw = and.Expressions.ToList();
                for (var i = 0; i < raw.Count; i++)
                {
                    if (raw[i] is not SemanticAggregateFilter aggregate ||
                        aggregate.Aggregate != SemanticFilterAggregate.Count ||
                        aggregate.Field is not null ||
                        aggregate.Predicate is not null ||
                        !IsCountExistenceComparison(aggregate))
                        continue;

                    for (var j = 0; j < raw.Count; j++)
                    {
                        if (i == j || raw[j] is not SemanticRelationshipFilter relationship ||
                            relationship.Quantifier != SemanticRelationshipQuantifier.Some ||
                            relationship.Relationship != aggregate.Relationship)
                            continue;

                        var pushed = aggregate with { Predicate = relationship.Predicate };
                        var remaining = new List<SemanticFilterExpression>();
                        for (var k = 0; k < raw.Count; k++)
                        {
                            if (k == i) remaining.Add(pushed);
                            else if (k != j) remaining.Add(raw[k]);
                        }

                        var merged = remaining.Count switch
                        {
                            0 => throw new InvalidOperationException(
                                "Aggregate equivalence normalization produced an empty AND expression."),
                            1 => remaining[0],
                            _ => new SemanticAndFilter(remaining)
                        };

                        return CanonicalizeAggregateRelationshipPushdown(merged);
                    }
                }

                return expressions.Count switch
                {
                    0 => throw new InvalidOperationException(
                        "Aggregate equivalence normalization produced an empty AND expression."),
                    1 => expressions[0],
                    _ => new SemanticAndFilter(expressions)
                };
            }

            case SemanticAggregateFilter aggregate when
                aggregate.Aggregate == SemanticFilterAggregate.Count &&
                aggregate.Field is null &&
                aggregate.Predicate is not null:
            {
                var strategy = AggregateExecutionStrategyResolver.Resolve(
                    aggregate.Operator, aggregate.Value);
                if (strategy is AggregateExecutionStrategy.CountExistsShortCircuit or
                    AggregateExecutionStrategy.CountEmptyShortCircuit)
                {
                    return new SemanticRelationshipFilter(
                        aggregate.Relationship,
                        strategy == AggregateExecutionStrategy.CountEmptyShortCircuit
                            ? SemanticRelationshipQuantifier.None
                            : SemanticRelationshipQuantifier.Some,
                        CanonicalizeAggregateRelationshipPushdown(aggregate.Predicate));
                }

                return aggregate with
                {
                    Predicate = CanonicalizeAggregateRelationshipPushdown(aggregate.Predicate)
                };
            }

            case SemanticOrFilter or:
            {
                var expressions = or.Expressions
                    .Select(CanonicalizeAggregateRelationshipPushdown)
                    .ToArray();
                return new SemanticOrFilter(expressions);
            }

            case SemanticRelationshipFilter relationship:
            {
                var predicate = CanonicalizeAggregateRelationshipPushdown(relationship.Predicate);
                return ReferenceEquals(predicate, relationship.Predicate)
                    ? relationship
                    : relationship with { Predicate = predicate };
            }

            default:
                return filter;
        }
    }

    private static bool IsCountExistenceComparison(SemanticAggregateFilter aggregate)
    {
        if (!TryGetIntegral(aggregate.Value, out var value))
            return false;

        return aggregate.Operator switch
        {
            SemanticAggregateFilterOperator.Gt when value == 0 => true,
            SemanticAggregateFilterOperator.Gte when value == 1 => true,
            SemanticAggregateFilterOperator.Neq when value == 0 => true,
            _ => false
        };
    }

    private static bool TryGetIntegral(object? value, out long result)
    {
        switch (value)
        {
            case byte v:
                result = v;
                return true;
            case sbyte v:
                result = v;
                return true;
            case short v:
                result = v;
                return true;
            case ushort v:
                result = v;
                return true;
            case int v:
                result = v;
                return true;
            case uint v:
                result = v;
                return true;
            case ulong v when v <= long.MaxValue:
                result = (long)v;
                return true;
            case long v:
                result = v;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static IReadOnlyList<IReadOnlyList<SemanticFilterExpression>>? ToDnf(
        SemanticFilterExpression expression,
        int maxTerms)
    {
        switch (expression)
        {
            case SemanticAndFilter and:
            {
                IReadOnlyList<SemanticFilterExpression> emptyTerm = Array.Empty<SemanticFilterExpression>();
                var result = new List<IReadOnlyList<SemanticFilterExpression>> { emptyTerm };
                foreach (var child in and.Expressions)
                {
                    var childTerms = ToDnf(child, maxTerms);
                    if (childTerms is null) return null;
                    var next = new List<IReadOnlyList<SemanticFilterExpression>>();
                    foreach (var left in result)
                    foreach (var right in childTerms)
                    {
                        var combined = left.Concat(right).ToArray();
                        next.Add(combined);
                        if (next.Count > maxTerms) return null;
                    }

                    result = next;
                }

                return result;
            }
            case SemanticOrFilter or:
            {
                var result = new List<IReadOnlyList<SemanticFilterExpression>>();
                foreach (var child in or.Expressions)
                {
                    var childTerms = ToDnf(child, maxTerms);
                    if (childTerms is null) return null;
                    result.AddRange(childTerms);
                    if (result.Count > maxTerms) return null;
                }

                return result;
            }
            default:
                return [[expression]];
        }
    }

    private static void AppendCommutativeFilter(
        StringBuilder builder,
        string kind,
        IReadOnlyList<SemanticFilterExpression> expressions)
    {
        var keys = expressions.Select(CreateFilterKey)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        builder.Append(kind).Append('[');
        foreach (var key in keys)
            builder.Append(key).Append(',');
        builder.Append(']');
    }

    private static string CreateFilterKey(SemanticFilterExpression expression)
    {
        var builder = new StringBuilder(128);
        AppendFilter(builder, expression);
        return builder.ToString();
    }

    private static void AppendAuthorization(StringBuilder builder, AuthorizationPredicate? predicate)
    {
        if (predicate is null)
            return;

        var canonical = CanonicalAuthorization(predicate);
        AppendAuthorizationNode(builder, canonical);
    }

    private static AuthorizationPredicate CanonicalAuthorization(AuthorizationPredicate predicate)
    {
        var left = predicate.Left is null ? null : CanonicalAuthorization(predicate.Left);
        var right = predicate.Right is null ? null : CanonicalAuthorization(predicate.Right);
        var current = predicate with { Left = left, Right = right };

        if (current.Kind == AuthorizationPredicateKind.Not &&
            current.Left?.Kind == AuthorizationPredicateKind.Not &&
            current.Left.Left is not null)
            return CanonicalAuthorization(current.Left.Left);

        if (current.Kind is AuthorizationPredicateKind.And or AuthorizationPredicateKind.Or)
        {
            var operands = new List<AuthorizationPredicate>();
            FlattenAuthorization(current.Kind, current, operands);
            operands = operands
                .Select(CanonicalAuthorization)
                .GroupBy(CreateAuthorizationKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(CreateAuthorizationKey, StringComparer.Ordinal)
                .ToList();

            if (operands.Count == 1)
                return operands[0];

            AuthorizationPredicate result = operands[0];
            for (var i = 1; i < operands.Count; i++)
                result = current.Kind == AuthorizationPredicateKind.And
                    ? AuthorizationPredicate.And(result, operands[i])
                    : AuthorizationPredicate.Or(result, operands[i]);
            return result;
        }

        return current;
    }

    private static void FlattenAuthorization(
        AuthorizationPredicateKind kind,
        AuthorizationPredicate node,
        ICollection<AuthorizationPredicate> destination)
    {
        if (node.Kind == kind)
        {
            if (node.Left is not null)
                FlattenAuthorization(kind, node.Left, destination);
            if (node.Right is not null)
                FlattenAuthorization(kind, node.Right, destination);
            return;
        }

        destination.Add(node);
    }

    private static string CreateAuthorizationKey(AuthorizationPredicate predicate)
    {
        var builder = new StringBuilder(96);
        AppendAuthorizationNode(builder, predicate);
        return builder.ToString();
    }

    private static void AppendAuthorizationNode(StringBuilder builder, AuthorizationPredicate predicate)
    {
        builder.Append((byte)predicate.Kind).Append('(');
        AppendValue(builder, predicate.Name);
        builder.Append('|');
        AppendValue(builder, predicate.Value);
        builder.Append('|');
        if (predicate.Left is not null)
            AppendAuthorizationNode(builder, predicate.Left);
        builder.Append('|');
        if (predicate.Right is not null)
            AppendAuthorizationNode(builder, predicate.Right);
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