using System.Collections;
using System.Text;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Providers.Storage.Sql.Query;

internal static class SemanticQuerySqlWriter
{
    public static string? WriteWhere(
        SemanticFilterExpression? filter,
        EntityMetadata entity,
        string alias,
        ICollection<SqlParameterBinding> parameters,
        IMetadataProvider metadata,
        AggregateExecutionStrategy aggregateStrategy = AggregateExecutionStrategy.Default)
    {
        if (filter is null) return null;

        // Start numbering after any parameters the caller already added to
        // this shared collection (e.g. SET-clause bindings in
        // SqlMutationCompiler.CompileUpdate). Always starting at 0 here
        // produces "@p0", "@p1", ... names that collide with those
        // pre-existing bindings: the parameters collection ends up with two
        // different values bound to the same parameter name, and whichever
        // one the provider resolves first silently answers for both
        // placeholders (e.g. a bigint COUNT(*) comparison ends up bound to
        // an unrelated text SET value, producing PostgreSQL error 42883
        // "operator does not exist: bigint > text").
        var nextParameter = parameters.Count;
        var nextAlias = 0;
        return WriteFilter(filter, entity, alias, parameters, metadata, aggregateStrategy, true, ref nextParameter,
            ref nextAlias);
    }

    public static string? WriteOrder(
        IReadOnlyList<SemanticOrderTerm> terms,
        EntityMetadata entity,
        string alias)
    {
        if (terms.Count == 0) return null;
        var parts = new List<string>();
        foreach (var term in terms)
        {
            var field = entity.EffectiveFields.FirstOrDefault(x => x.Id == term.Field)
                        ?? throw new InvalidOperationException(
                            $"Unknown order field '{term.Field}' on '{entity.Name}'.");
            var column = ResolveColumn(entity, field);
            parts.Add(
                $"{SqlCompiler.QuoteIdentifier(alias)}.{SqlCompiler.QuoteIdentifier(column.EffectiveStorageName)} {(term.Direction == SemanticSortDirection.Desc ? "DESC" : "ASC")}");
        }

        return "ORDER BY " + string.Join(", ", parts);
    }

    private static string WriteFilter(
        SemanticFilterExpression expression,
        EntityMetadata entity,
        string alias,
        ICollection<SqlParameterBinding> parameters,
        IMetadataProvider metadata,
        AggregateExecutionStrategy aggregateStrategy,
        bool allowAggregateStrategy,
        ref int nextParameter,
        ref int nextAlias)
    {
        switch (expression)
        {
            case SemanticFieldFilter field:
            {
                var fieldMetadata = entity.EffectiveFields.FirstOrDefault(x => x.Id == field.Field)
                                    ?? throw new InvalidOperationException(
                                        $"Unknown filter field '{field.Field}' on '{entity.Name}'.");
                var column = ResolveColumn(entity, fieldMetadata);
                var reference =
                    $"{SqlCompiler.QuoteIdentifier(alias)}.{SqlCompiler.QuoteIdentifier(column.EffectiveStorageName)}";

                if (field.Operator == SemanticFilterOperator.Eq && field.Value is null) return reference + " IS NULL";
                if (field.Operator == SemanticFilterOperator.Neq && field.Value is null)
                    return reference + " IS NOT NULL";

                if (field.Operator == SemanticFilterOperator.In)
                {
                    var values = NormalizeList(field.Value);
                    if (values.Count == 0) return "1 = 0";
                    var refs = new List<string>();
                    foreach (var value in values)
                    {
                        var name = "p" + nextParameter++;
                        parameters.Add(new SqlParameterBinding(name, value, ClrType: fieldMetadata.ClrType));
                        refs.Add("@" + name);
                    }

                    return $"{reference} IN ({string.Join(", ", refs)})";
                }

                var parameter = "p" + nextParameter++;
                parameters.Add(new SqlParameterBinding(parameter, field.Value, ClrType: fieldMetadata.ClrType));
                return reference + (field.Operator == SemanticFilterOperator.Neq ? " <> " : " = ") + "@" + parameter;
            }

            case SemanticRelationshipFilter relationshipFilter:
                return WriteRelationshipFilter(
                    relationshipFilter,
                    entity,
                    alias,
                    parameters,
                    metadata,
                    aggregateStrategy,
                    ref nextParameter,
                    ref nextAlias);

            case SemanticAggregateFilter aggregateFilter:
                return WriteAggregateFilter(
                    aggregateFilter,
                    entity,
                    alias,
                    parameters,
                    metadata,
                    aggregateStrategy,
                    allowAggregateStrategy,
                    ref nextParameter,
                    ref nextAlias);

            case SemanticAndFilter andFilter:
                return Join(andFilter.Expressions, "AND", entity, alias, parameters, metadata, aggregateStrategy,
                    allowAggregateStrategy, ref nextParameter, ref nextAlias);

            case SemanticOrFilter orFilter:
                return Join(orFilter.Expressions, "OR", entity, alias, parameters, metadata, aggregateStrategy,
                    allowAggregateStrategy, ref nextParameter, ref nextAlias);

            default:
                throw new NotSupportedException(expression.GetType().Name);
        }
    }

    private static string WriteRelationshipFilter(
        SemanticRelationshipFilter filter,
        EntityMetadata source,
        string sourceAlias,
        ICollection<SqlParameterBinding> parameters,
        IMetadataProvider metadata,
        AggregateExecutionStrategy aggregateStrategy,
        ref int nextParameter,
        ref int nextAlias)
    {
        var relationship = metadata.GetRelationship(filter.Relationship);
        if (relationship.Source != source.EntityId)
            throw new InvalidOperationException(
                $"Relationship '{relationship.Name}' is not a relationship from '{source.Name}'.");
        var target = metadata.GetEntity(relationship.Target);
        var targetAlias = "s" + nextAlias++;
        var join = RenderJoinCondition(relationship.SourceKey, relationship.TargetKey, source, sourceAlias, target,
            targetAlias);
        var predicate = WriteFilter(
            filter.Predicate,
            target,
            targetAlias,
            parameters,
            metadata,
            aggregateStrategy,
            false,
            ref nextParameter,
            ref nextAlias);

        var body = $"{join} AND {predicate}";
        var exists =
            $"EXISTS (SELECT 1 FROM {SqlCompiler.QuoteStorageName(target.EffectiveStorageName)} {SqlCompiler.QuoteIdentifier(targetAlias)} WHERE {body})";

        return filter.Quantifier switch
        {
            SemanticRelationshipQuantifier.Some => exists,
            SemanticRelationshipQuantifier.None => $"NOT {exists}",
            SemanticRelationshipQuantifier.All =>
                $"NOT EXISTS (SELECT 1 FROM {SqlCompiler.QuoteStorageName(target.EffectiveStorageName)} {SqlCompiler.QuoteIdentifier(targetAlias)} WHERE {join} AND NOT ({predicate}))",
            _ => throw new NotSupportedException(filter.Quantifier.ToString())
        };
    }

    private static string WriteAggregateFilter(
        SemanticAggregateFilter filter,
        EntityMetadata source,
        string sourceAlias,
        ICollection<SqlParameterBinding> parameters,
        IMetadataProvider metadata,
        AggregateExecutionStrategy aggregateStrategy,
        bool allowAggregateStrategy,
        ref int nextParameter,
        ref int nextAlias)
    {
        var relationship = metadata.GetRelationship(filter.Relationship);
        if (relationship.Source != source.EntityId)
            throw new InvalidOperationException(
                $"Relationship '{relationship.Name}' is not a relationship from '{source.Name}'.");
        var target = metadata.GetEntity(relationship.Target);
        var targetAlias = "a" + nextAlias++;
        var join = RenderJoinCondition(relationship.SourceKey, relationship.TargetKey, source, sourceAlias, target,
            targetAlias);

        // The planner-level AggregateCardinalityOptimizationRule hint proves this bare COUNT
        // comparison depends only on whether the related collection is empty, not on the
        // exact count. Rendering it as EXISTS/NOT EXISTS lets the database avoid materializing
        // a count at all; the semantic meaning (and required security invariants, unaffected
        // by this rewrite) are exactly the same as the COUNT-subquery form below.
        if (allowAggregateStrategy && AggregateExecutionStrategyResolver.IsEligibleFor(filter, aggregateStrategy))
        {
            var existsExpression =
                $"EXISTS (SELECT 1 FROM {SqlCompiler.QuoteStorageName(target.EffectiveStorageName)} {SqlCompiler.QuoteIdentifier(targetAlias)} WHERE {join})";
            return aggregateStrategy == AggregateExecutionStrategy.CountExistsShortCircuit
                ? existsExpression
                : $"NOT {existsExpression}";
        }

        var aggregatePredicate = filter.Predicate is null
            ? null
            : WriteFilter(filter.Predicate, target, targetAlias, parameters, metadata, aggregateStrategy, false,
                ref nextParameter, ref nextAlias);
        var where = aggregatePredicate is null ? join : $"{join} AND {aggregatePredicate}";

        string expression;
        switch (filter.Aggregate)
        {
            case SemanticFilterAggregate.Count:
                expression =
                    $"(SELECT COUNT(*) FROM {SqlCompiler.QuoteStorageName(target.EffectiveStorageName)} {SqlCompiler.QuoteIdentifier(targetAlias)} WHERE {where})";
                break;

            case SemanticFilterAggregate.Min:
            case SemanticFilterAggregate.Max:
                if (filter.Field is null)
                    throw new InvalidOperationException(
                        $"{filter.Aggregate} aggregate filter requires a target field.");
                var field = target.EffectiveFields.FirstOrDefault(x => x.Id == filter.Field.Value)
                            ?? throw new InvalidOperationException(
                                $"Unknown aggregate filter field '{filter.Field}' on '{target.Name}'.");
                var column = ResolveColumn(target, field);
                var aggregateName = filter.Aggregate == SemanticFilterAggregate.Min ? "MIN" : "MAX";
                expression =
                    $"(SELECT {aggregateName}({SqlCompiler.QuoteIdentifier(targetAlias)}.{SqlCompiler.QuoteIdentifier(column.EffectiveStorageName)}) FROM {SqlCompiler.QuoteStorageName(target.EffectiveStorageName)} {SqlCompiler.QuoteIdentifier(targetAlias)} WHERE {where})";
                break;

            default:
                throw new NotSupportedException(filter.Aggregate.ToString());
        }

        var parameter = "p" + nextParameter++;

        // PostgreSQL COUNT(*) is bigint. Keep the parameter on the same
        // numeric type so comparisons such as COUNT(*) > 0 are resolved as
        // bigint > bigint rather than bigint > text by Npgsql/PostgreSQL.
        var comparisonValue = filter.Aggregate == SemanticFilterAggregate.Count
            ? ConvertCountComparisonValue(filter.Value)
            : filter.Value;

        parameters.Add(new SqlParameterBinding(parameter, comparisonValue));
        return expression + RenderAggregateOperator(filter.Operator) + "@" + parameter;
    }

    private static long ConvertCountComparisonValue(object? value)
    {
        if (value is null)
            throw new InvalidOperationException("A Count aggregate filter requires a comparison value.");

        try
        {
            return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidOperationException(
                $"Count aggregate filter value '{value}' is not an integer.",
                ex);
        }
    }

    private static string RenderAggregateOperator(SemanticAggregateFilterOperator op) => op switch
    {
        SemanticAggregateFilterOperator.Eq => " = ",
        SemanticAggregateFilterOperator.Neq => " <> ",
        SemanticAggregateFilterOperator.Gt => " > ",
        SemanticAggregateFilterOperator.Gte => " >= ",
        SemanticAggregateFilterOperator.Lt => " < ",
        SemanticAggregateFilterOperator.Lte => " <= ",
        _ => throw new NotSupportedException(op.ToString())
    };

    private static string RenderJoinCondition(
        ColumnReference sourceReference,
        ColumnReference targetReference,
        EntityMetadata source,
        string sourceAlias,
        EntityMetadata target,
        string targetAlias)
    {
        return $"{RenderReference(sourceReference, source, sourceAlias, target, targetAlias)} = " +
               $"{RenderReference(targetReference, source, sourceAlias, target, targetAlias)}";
    }

    private static string RenderReference(
        ColumnReference reference,
        EntityMetadata source,
        string sourceAlias,
        EntityMetadata target,
        string targetAlias)
    {
        var entity = reference.EntityId == source.EntityId ? source :
            reference.EntityId == target.EntityId ? target :
            throw new InvalidOperationException("Relationship join references an entity outside its endpoints.");
        var alias = reference.EntityId == source.EntityId ? sourceAlias : targetAlias;
        var column = entity.Columns.FirstOrDefault(x => x.Id == reference.ColumnId)
                     ?? throw new InvalidOperationException(
                         $"Entity '{entity.Name}' has no column '{reference.ColumnId}'.");
        return $"{SqlCompiler.QuoteIdentifier(alias)}.{SqlCompiler.QuoteIdentifier(column.EffectiveStorageName)}";
    }

    private static string Join(
        IReadOnlyList<SemanticFilterExpression> expressions,
        string op,
        EntityMetadata entity,
        string alias,
        ICollection<SqlParameterBinding> parameters,
        IMetadataProvider metadata,
        AggregateExecutionStrategy aggregateStrategy,
        bool allowAggregateStrategy,
        ref int nextParameter,
        ref int nextAlias)
    {
        if (expressions.Count == 0) throw new InvalidOperationException("Filter group cannot be empty.");
        var parts = new List<string>(expressions.Count);
        foreach (var expression in expressions)
            parts.Add(WriteFilter(expression, entity, alias, parameters, metadata, aggregateStrategy,
                allowAggregateStrategy, ref nextParameter, ref nextAlias));
        return "(" + string.Join(" " + op + " ", parts) + ")";
    }

    private static IReadOnlyList<object?> NormalizeList(object? value) => value switch
    {
        null => [],
        object?[] array => array,
        Array array => array.Cast<object?>().ToArray(),
        IReadOnlyList<object?> list => list,
        IEnumerable enumerable when value is not string => enumerable.Cast<object?>().ToArray(),
        _ => [value]
    };

    private static ColumnMetadata ResolveColumn(EntityMetadata entity, FieldMetadata field) =>
        field.Column is null
            ? throw new InvalidOperationException($"Field '{entity.Name}.{field.Name}' has no storage column mapping.")
            : entity.Columns.FirstOrDefault(x => x.Id == field.Column.ColumnId)
              ?? throw new InvalidOperationException(
                  $"Field '{entity.Name}.{field.Name}' references a missing column.");
}