using System.Text;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.Storage.Sql.Query;
using Foundgine.Core.Semantic.Security;
using Foundgine.Providers.Storage.Sql.Security;

namespace Foundgine.Providers.Storage.Sql;

/// <summary>
/// Compiles the provider-independent Execution IR into SQL, including
/// filtering, ordering, aggregation, and cursor pagination.
/// </summary>
public sealed class SqlCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
    IProviderSecurityConformanceEvaluator
{
    private readonly IMetadataProvider _metadata;

    public SqlCompiler(IMetadataProvider metadata) =>
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

    public IReadOnlyCollection<string> PreservedSecurityInvariants =>
    [
        SecurityInvariantIds.AuthorizationRequired,
        SecurityInvariantIds.RuntimeAuthorization,
        SecurityInvariantIds.FieldVisibility,
        SecurityInvariantIds.RelationshipVisibility,
        SecurityInvariantIds.ParameterizedValues,
        SecurityInvariantIds.PlanCacheContextIsolation
    ];

    /// <summary>Compatibility bridge for existing callers that still hold a semantic plan.
    /// The semantic plan is lowered immediately into provider-neutral Execution IR;
    /// no provider-specific information is introduced at this boundary.</summary>
    public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan is not SqlPlan sqlPlan)
            throw new ArgumentException("Expected a SqlPlan.", nameof(plan));

        var result = SqlSecurityConformance.Verify(ir, sqlPlan);
        return new ProviderSecurityConformanceResult(
            sqlPlan.Provider,
            result.Required,
            result.Satisfied,
            result.Violations);
    }

    public SqlPlan Compile(SemanticPlan plan) => Compile(ExecutionIRCompiler.Compile(plan));

    public SqlPlan Compile(ExecutionIR ir)
    {
        ArgumentNullException.ThrowIfNull(ir);

        var occurrences = new List<NodeOccurrence>();
        Collect(ir.Root, occurrences);
        var aliases = occurrences.ToDictionary(x => x.Node.Id, x => $"t{x.Node.Id}");
        var select = new List<string>();
        var bindings = new List<SqlColumnBinding>();
        var authorization = occurrences
            .Where(x => x.Node.Authorization is not null)
            .Select(x => new SqlAuthorizationPredicate(x.Node.Id, x.Node.Authorization!))
            .ToArray();

        foreach (var occurrence in occurrences)
        {
            var entity = _metadata.GetEntity(occurrence.Node.EntityId);
            if (occurrence.Node.Fields.Count == 0)
            {
                // Empty fields are intentionally fail-closed. An authorization
                // policy may remove every requested field; treating an empty
                // post-authorization selection as "select all" would turn a
                // denied request into a data disclosure.
                throw new InvalidOperationException(
                    $"Execution node {occurrence.Node.Id} selects no fields after semantic authorization.");
            }

            var fields = occurrence.Node.Fields;

            foreach (var fieldId in fields)
                AddFieldSelection(occurrence.Node, entity, fieldId, aliases, select, bindings);
        }

        if (select.Count == 0)
            throw new InvalidOperationException("The execution IR selects no fields.");

        var parameters = new List<SqlParameterBinding>();
        var root = occurrences[0];
        var rootEntity = _metadata.GetEntity(root.Node.EntityId);
        var rootOptions = root.Node.QueryOptions;
        var requestedOrder = rootOptions?.EffectiveOrder ?? [];
        var hasCursor = rootOptions?.Limit is > 0 && rootOptions.After is not null;
        var hasForwardPagination = rootOptions?.Limit is > 0 && rootOptions.Offset is null;

        if (rootOptions?.After is not null && rootOptions.Limit is not > 0)
            throw new InvalidOperationException("Cursor pagination requires a positive first/limit value.");

        if (rootOptions?.After is not null && rootOptions.Offset is not null)
            throw new NotSupportedException("Cursor pagination cannot be combined with offset pagination.");

        if (rootOptions?.Limit is < 0 || rootOptions?.Offset is < 0)
            throw new InvalidOperationException("Query limit and offset must be non-negative.");

        SqlPaginationPlan? pagination = null;
        IReadOnlyList<SemanticOrderTerm> effectiveCursorOrder = Array.Empty<SemanticOrderTerm>();
        var resolvedOrder = new List<ResolvedOrderTerm>();

        if (hasForwardPagination)
        {
            effectiveCursorOrder = BuildCursorOrder(rootEntity, requestedOrder);
        }

        var orderTerms = hasForwardPagination ? effectiveCursorOrder : requestedOrder;
        foreach (var term in orderTerms)
        {
            ExecutionIRNode orderNode;
            EntityMetadata orderEntity;
            FieldMetadata field;

            if (term.IsAggregate)
            {
                if (term.EffectivePath.Count != 1)
                    throw new NotSupportedException("Collection aggregation currently supports one relationship hop.");

                orderNode = ResolveOrderParentNode(root.Node, term.EffectivePath);
                orderEntity = _metadata.GetEntity(_metadata.GetRelationship(term.EffectivePath[0]).Target);
                field = orderEntity.EffectiveFields.FirstOrDefault(x => x.Id == term.Field)
                        ?? throw new InvalidOperationException(
                            $"Unknown aggregate order field '{term.Field}' on '{orderEntity.Name}'.");
            }
            else
            {
                orderNode = ResolveOrderNode(root.Node, term.EffectivePath);
                orderEntity = _metadata.GetEntity(orderNode.EntityId);
                field = orderEntity.EffectiveFields.FirstOrDefault(x => x.Id == term.Field)
                        ?? throw new InvalidOperationException(
                            $"Unknown order field '{term.Field}' on '{orderEntity.Name}'.");
            }

            if (!term.IsAggregate && field.Column is null)
                throw new InvalidOperationException(
                    $"Order field '{orderEntity.Name}.{field.Name}' has no storage column mapping.");

            if (term.IsAggregate && term.EffectivePath.Count == 0)
                throw new InvalidOperationException("Aggregate ordering requires a collection relationship path.");

            var resolved = new ResolvedOrderTerm(term, orderNode, orderEntity, aliases[orderNode.Id], field);
            resolvedOrder.Add(resolved);

            if (hasForwardPagination)
            {
                var cursorBindings = pagination?.CursorValues is not null
                    ? pagination.CursorValues.ToList()
                    : new List<SqlCursorBinding>();

                if (term.IsAggregate)
                {
                    AddHiddenAggregateCursorSelection(
                        term,
                        orderNode,
                        orderEntity,
                        field,
                        aliases,
                        select,
                        bindings,
                        cursorBindings);
                }
                else
                {
                    AddHiddenCursorSelection(
                        orderNode,
                        orderEntity,
                        field,
                        term.Direction,
                        aliases,
                        select,
                        bindings,
                        cursorBindings);
                }

                pagination = new SqlPaginationPlan(
                    rootOptions!.Limit!.Value,
                    cursorBindings,
                    rootOptions.After);
            }
        }

        var sql = new StringBuilder();
        sql.Append("SELECT ").Append(string.Join(", ", select));
        sql.Append(" FROM ").Append(QuoteStorageName(rootEntity.EffectiveStorageName));
        sql.Append(" ").Append(QuoteIdentifier(aliases[root.Node.Id]));

        foreach (var occurrence in occurrences.Skip(1))
        {
            if (occurrence.Node.ViaRelationship is null)
                throw new InvalidOperationException($"Node {occurrence.Node.Id} has no relationship to its parent.");

            var relationship = _metadata.GetRelationship(occurrence.Node.ViaRelationship.Value);
            var parentNode = occurrences.First(x => x.Node.Id == occurrence.ParentId).Node;
            var left = RenderJoinColumn(relationship.SourceKey, parentNode, occurrence.Node, aliases);
            var right = RenderJoinColumn(relationship.TargetKey, parentNode, occurrence.Node, aliases);

            sql.Append(" INNER JOIN ")
                .Append(QuoteStorageName(_metadata.GetEntity(occurrence.Node.EntityId).EffectiveStorageName))
                .Append(" ").Append(QuoteIdentifier(aliases[occurrence.Node.Id]))
                .Append(" ON ").Append(left).Append(" = ").Append(right);
        }

        var where = SemanticQuerySqlWriter.WriteWhere(
            rootOptions?.Filter,
            rootEntity,
            aliases[root.Node.Id],
            parameters,
            _metadata,
            root.Node.AggregateExecutionStrategy);

        foreach (var occurrence in occurrences)
        {
            if (occurrence.Node.Authorization is null)
                continue;

            var entity = _metadata.GetEntity(occurrence.Node.EntityId);
            var predicateSql = Query.SqlAuthorizationWriter.Write(
                occurrence.Node.Authorization,
                entity,
                aliases[occurrence.Node.Id],
                parameters);

            where = string.IsNullOrWhiteSpace(where)
                ? predicateSql
                : $"({where}) AND ({predicateSql})";
        }

        if (hasCursor)
        {
            var cursorValues = CursorCodec.Decode(rootOptions!.After!);
            if (cursorValues.Count != effectiveCursorOrder.Count)
            {
                throw new InvalidOperationException(
                    $"The pagination cursor contains {cursorValues.Count} values, but the current ordering requires {effectiveCursorOrder.Count}.");
            }

            var seek = BuildSeekPredicate(
                resolvedOrder,
                cursorValues,
                parameters);

            where = string.IsNullOrWhiteSpace(where)
                ? seek
                : $"({where}) AND ({seek})";
        }

        if (!string.IsNullOrWhiteSpace(where))
            sql.Append(" WHERE ").Append(where);

        if (hasForwardPagination)
        {
            var order = WriteResolvedOrder(resolvedOrder);

            sql.Append(" ").Append(order);
            parameters.Add(new SqlParameterBinding(
                "__fg_limit",
                null,
                ContextPath: ExecutionContextKeys.PaginationLimit));
            sql.Append(" LIMIT @__fg_limit");
        }
        else
        {
            var order = WriteResolvedOrder(resolvedOrder);

            if (!string.IsNullOrWhiteSpace(order))
                sql.Append(" ").Append(order);

            if (rootOptions?.Offset is { } offset && rootOptions.Limit is null)
                sql.Append(" LIMIT -1");
            else if (rootOptions?.Limit is not null)
            {
                parameters.Add(new SqlParameterBinding(
                    "__fg_limit",
                    null,
                    ContextPath: ExecutionContextKeys.PaginationLimit));
                sql.Append(" LIMIT @__fg_limit");
            }

            if (rootOptions?.Offset is not null)
            {
                parameters.Add(new SqlParameterBinding(
                    "__fg_offset",
                    null,
                    ContextPath: ExecutionContextKeys.PaginationOffset));
                sql.Append(" OFFSET @__fg_offset");
            }
        }

        var compiledPlan = new SqlPlan(sql.ToString(), bindings, parameters, pagination, authorization);
        ExecutionIRBoundary.BindProviderPlan(ir, compiledPlan);
        if (ir.RequiredSecurityInvariants.Count > 0)
            SqlSecurityConformance.EnsureSatisfied(ir, compiledPlan);

        return compiledPlan;
    }

    ProviderPlan IProviderPlanCompiler.Compile(ExecutionIR ir) => Compile(ir);

    // Compatibility adapter for callers still holding the legacy plan.

    private static void AddFieldSelection(
        ExecutionIRNode node,
        EntityMetadata entity,
        FieldId fieldId,
        IReadOnlyDictionary<int, string> aliases,
        ICollection<string> select,
        ICollection<SqlColumnBinding> bindings)
    {
        var field = entity.EffectiveFields.FirstOrDefault(x => x.Id == fieldId)
                    ?? throw new InvalidOperationException(
                        $"Unknown field '{fieldId}' on entity '{entity.Name}'.");

        if (field.Column is null)
            throw new InvalidOperationException(
                $"Field '{entity.Name}.{field.Name}' has no storage column mapping.");

        var column = entity.Columns.FirstOrDefault(x => x.Id == field.Column.ColumnId)
                     ?? throw new InvalidOperationException(
                         $"Field '{entity.Name}.{field.Name}' references a missing column '{field.Column.ColumnId}'.");

        var resultName = $"__fg_{node.Id}_{field.Name}";
        select.Add(
            $"{QuoteIdentifier(aliases[node.Id])}.{QuoteIdentifier(column.EffectiveStorageName)} AS {QuoteIdentifier(resultName)}");

        bindings.Add(new SqlColumnBinding(
            resultName,
            entity.EntityId,
            field.Id,
            column.EffectiveStorageName,
            node.Id));
    }

    private void AddHiddenAggregateCursorSelection(
        SemanticOrderTerm term,
        ExecutionIRNode node,
        EntityMetadata entity,
        FieldMetadata field,
        IReadOnlyDictionary<int, string> aliases,
        ICollection<string> select,
        ICollection<SqlColumnBinding> bindings,
        ICollection<SqlCursorBinding> cursorBindings)
    {
        var expression = BuildAggregateReference(term, node, entity, field, aliases);
        var resultName = $"__fg_cursor_agg_{node.Id}_{field.Name}_{term.Aggregate}";

        select.Add($"{expression} AS {QuoteIdentifier(resultName)}");
        var cursorType = term.Aggregate == SemanticOrderAggregate.Count ? typeof(long) : field.ClrType;

        var targetEntityId = _metadata.GetRelationship(term.EffectivePath[0]).Target;
        bindings.Add(new SqlColumnBinding(
            resultName,
            targetEntityId,
            field.Id,
            term.Aggregate.ToString(),
            node.Id,
            IsCursor: true));

        cursorBindings.Add(new SqlCursorBinding(
            resultName,
            targetEntityId,
            field.Id,
            cursorType,
            term.Direction));
    }

    private static void AddHiddenCursorSelection(
        ExecutionIRNode node,
        EntityMetadata entity,
        FieldMetadata field,
        SemanticSortDirection direction,
        IReadOnlyDictionary<int, string> aliases,
        ICollection<string> select,
        List<SqlColumnBinding> bindings,
        ICollection<SqlCursorBinding> cursorBindings)
    {
        if (field.Column is null)
            throw new InvalidOperationException(
                $"Order field '{entity.Name}.{field.Name}' has no storage column mapping.");

        var column = entity.Columns.FirstOrDefault(x => x.Id == field.Column.ColumnId)
                     ?? throw new InvalidOperationException(
                         $"Order field '{entity.Name}.{field.Name}' references a missing column.");

        var resultName = $"__fg_cursor_{node.Id}_{field.Name}";
        var alreadySelected = bindings.Any(x =>
            x.NodeId == node.Id &&
            x.FieldId == field.Id);

        if (!alreadySelected)
        {
            select.Add(
                $"{QuoteIdentifier(aliases[node.Id])}.{QuoteIdentifier(column.EffectiveStorageName)} AS {QuoteIdentifier(resultName)}");

            bindings.Add(new SqlColumnBinding(
                resultName,
                entity.EntityId,
                field.Id,
                column.EffectiveStorageName,
                node.Id,
                IsCursor: true));
        }
        else
        {
            var existing = bindings.First(x =>
                x.NodeId == node.Id &&
                x.FieldId == field.Id);

            resultName = existing.ResultName;
            var index = bindings.IndexOf(existing);
            bindings[index] = existing with { IsCursor = true };
        }

        cursorBindings.Add(new SqlCursorBinding(
            resultName,
            entity.EntityId,
            field.Id,
            field.ClrType,
            direction));
    }

    private static IReadOnlyList<SemanticOrderTerm> BuildCursorOrder(
        EntityMetadata entity,
        IReadOnlyList<SemanticOrderTerm> requested)
    {
        var result = requested.ToList();

        if (entity.PrimaryKey is null)
            throw new InvalidOperationException(
                $"Entity '{entity.Name}' has no primary-key metadata required for cursor pagination.");

        var primaryKeyField = entity.EffectiveFields.FirstOrDefault(f => f.Column == entity.PrimaryKey)
                              ?? throw new InvalidOperationException(
                                  $"Entity '{entity.Name}' primary key is not mapped to a semantic field.");

        // FieldId is not globally sufficient to identify an ordering term. A field
        // with the same semantic identity can occur on a related path. Cursor
        // pagination for this entity requires the primary key at the root path.
        if (!result.Any(x =>
                x.IsRootField && x.Aggregate == SemanticOrderAggregate.None && x.Field == primaryKeyField.Id))
            result.Add(new SemanticOrderTerm(primaryKeyField.Id, SemanticSortDirection.Asc));

        return result;
    }

    private string BuildSeekPredicate(
        IReadOnlyList<ResolvedOrderTerm> order,
        IReadOnlyList<System.Text.Json.JsonElement> cursorValues,
        ICollection<SqlParameterBinding> parameters)
    {
        if (order.Count == 0)
            throw new InvalidOperationException("Cursor pagination requires at least one ordering term.");

        var branches = new List<string>();

        for (var i = 0; i < order.Count; i++)
        {
            var prefix = new List<string>();

            for (var j = 0; j < i; j++)
            {
                var equality = BuildOrderReference(order[j]);
                var equalityParameter = AddCursorParameter(
                    cursorValues[j],
                    order[j].Field.ClrType,
                    parameters);
                prefix.Add($"{equality} = @{equalityParameter}");
            }

            var reference = BuildOrderReference(order[i]);
            var parameter = AddCursorParameter(
                cursorValues[i],
                order[i].Field.ClrType,
                parameters);

            var comparison = order[i].Term.Direction == SemanticSortDirection.Desc ? "<" : ">";
            prefix.Add($"{reference} {comparison} @{parameter}");
            branches.Add("(" + string.Join(" AND ", prefix) + ")");
        }

        return "(" + string.Join(" OR ", branches) + ")";
    }

    private string BuildOrderReference(ResolvedOrderTerm term)
    {
        if (term.Term.IsAggregate)
            return BuildAggregateReference(term.Term, term.Node, term.Entity, term.Field,
                new Dictionary<int, string> { [term.Node.Id] = term.Alias });

        var field = term.Field;
        if (field.Column is null)
            throw new InvalidOperationException(
                $"Order field '{term.Entity.Name}.{field.Name}' has no storage column mapping.");

        var column = term.Entity.Columns.FirstOrDefault(x => x.Id == field.Column.ColumnId)
                     ?? throw new InvalidOperationException(
                         $"Order field '{term.Entity.Name}.{field.Name}' references a missing column.");

        return $"{QuoteIdentifier(term.Alias)}.{QuoteIdentifier(column.EffectiveStorageName)}";
    }

    private string BuildAggregateReference(
        SemanticOrderTerm term,
        ExecutionIRNode sourceNode,
        EntityMetadata sourceEntity,
        FieldMetadata field,
        IReadOnlyDictionary<int, string> aliases)
    {
        if (term.EffectivePath.Count == 0)
            throw new InvalidOperationException("Aggregate ordering requires a relationship path.");

        if (term.EffectivePath.Count != 1)
            throw new NotSupportedException("Collection aggregation currently supports one relationship hop.");

        var relationshipId = term.EffectivePath[0];
        var relationship = _metadata.GetRelationship(relationshipId);
        var targetEntity = _metadata.GetEntity(relationship.Target);
        var targetAlias = "a" + sourceNode.Id + "_agg";
        var targetReference = relationship.TargetKey;
        var sourceReference = relationship.SourceKey;

        var targetColumn = targetEntity.Columns.FirstOrDefault(c => c.Id == targetReference.ColumnId)
                           ?? throw new InvalidOperationException(
                               $"Target entity '{targetEntity.Name}' has no join column '{targetReference.ColumnId}'.");
        var sourceColumn = sourceEntity.Columns.FirstOrDefault(c => c.Id == sourceReference.ColumnId)
                           ?? throw new InvalidOperationException(
                               $"Source entity '{sourceEntity.Name}' has no join column '{sourceReference.ColumnId}'.");

        var correlation = $"{QuoteIdentifier(targetAlias)}.{QuoteIdentifier(targetColumn.EffectiveStorageName)} = " +
                          $"{QuoteIdentifier(aliases[sourceNode.Id])}.{QuoteIdentifier(sourceColumn.EffectiveStorageName)}";

        string aggregate;
        if (term.Aggregate == SemanticOrderAggregate.Count)
        {
            aggregate = "COUNT(*)";
        }
        else
        {
            if (field.Column is null)
                throw new InvalidOperationException(
                    $"Aggregate field '{targetEntity.Name}.{field.Name}' has no storage column mapping.");

            var valueColumn = targetEntity.Columns.FirstOrDefault(c => c.Id == field.Column.ColumnId)
                              ?? throw new InvalidOperationException(
                                  $"Aggregate field '{targetEntity.Name}.{field.Name}' references a missing column.");

            aggregate =
                $"{(term.Aggregate == SemanticOrderAggregate.Min ? "MIN" : "MAX")}({QuoteIdentifier(targetAlias)}.{QuoteIdentifier(valueColumn.EffectiveStorageName)})";
        }

        return
            $"(SELECT {aggregate} FROM {QuoteStorageName(targetEntity.EffectiveStorageName)} {QuoteIdentifier(targetAlias)} WHERE {correlation})";
    }

    private static string AddCursorParameter(
        System.Text.Json.JsonElement value,
        Type type,
        ICollection<SqlParameterBinding> parameters)
    {
        var parameter = "p" + parameters.Count;
        var converted = Query.CursorCodec.ConvertValue(value, type);
        parameters.Add(new SqlParameterBinding(parameter, converted));
        return parameter;
    }

    private string WriteResolvedOrder(IReadOnlyList<ResolvedOrderTerm> terms)
    {
        if (terms.Count == 0)
            return string.Empty;

        var parts = terms.Select(term =>
        {
            if (term.Term.IsAggregate)
            {
                var expression = BuildAggregateReference(
                    term.Term,
                    term.Node,
                    term.Entity,
                    term.Field,
                    new Dictionary<int, string> { [term.Node.Id] = term.Alias });
                return $"{expression} " + (term.Term.Direction == SemanticSortDirection.Desc ? "DESC" : "ASC");
            }

            if (term.Field.Column is null)
                throw new InvalidOperationException(
                    $"Order field '{term.Entity.Name}.{term.Field.Name}' has no storage column mapping.");

            var column = term.Entity.Columns.First(x => x.Id == term.Field.Column.ColumnId);
            return $"{QuoteIdentifier(term.Alias)}.{QuoteIdentifier(column.EffectiveStorageName)} " +
                   (term.Term.Direction == SemanticSortDirection.Desc ? "DESC" : "ASC");
        });

        return "ORDER BY " + string.Join(", ", parts);
    }

    private ExecutionIRNode ResolveOrderParentNode(ExecutionIRNode root, IReadOnlyList<RelationshipId> path)
    {
        if (path.Count == 0)
            return root;

        var current = root;
        for (var i = 0; i < path.Count - 1; i++)
        {
            var relationshipId = path[i];
            current = current.Children.FirstOrDefault(x => x.ViaRelationship == relationshipId)
                      ?? throw new InvalidOperationException(
                          $"Order path relationship '{relationshipId}' is not part of the execution IR.");
        }

        return current;
    }

    private ExecutionIRNode ResolveOrderNode(ExecutionIRNode root, IReadOnlyList<RelationshipId> path)
    {
        var current = root;
        foreach (var relationshipId in path)
        {
            current = current.Children.FirstOrDefault(x => x.ViaRelationship == relationshipId)
                      ?? throw new InvalidOperationException(
                          $"Order path relationship '{relationshipId}' is not part of the execution IR. " +
                          "The relationship must be selected before it can be used for ordering.");
        }

        return current;
    }

    private sealed record ResolvedOrderTerm(
        SemanticOrderTerm Term,
        ExecutionIRNode Node,
        EntityMetadata Entity,
        string Alias,
        FieldMetadata Field);

    private static void Collect(ExecutionIRNode node, ICollection<NodeOccurrence> result, int? parentId = null)
    {
        result.Add(new NodeOccurrence(node, parentId));
        foreach (var child in node.Children)
            Collect(child, result, node.Id);
    }

    private string RenderJoinColumn(
        ColumnReference reference,
        ExecutionIRNode parent,
        ExecutionIRNode child,
        IReadOnlyDictionary<int, string> aliases)
    {
        var node = parent.EntityId == reference.EntityId
            ? parent
            : child.EntityId == reference.EntityId
                ? child
                : throw new InvalidOperationException(
                    $"Join column entity '{reference.EntityId}' does not match the relationship endpoints.");

        var entity = _metadata.GetEntity(reference.EntityId);
        var column = entity.Columns.FirstOrDefault(x => x.Id == reference.ColumnId)
                     ?? throw new InvalidOperationException(
                         $"Entity '{entity.Name}' has no column '{reference.ColumnId}'.");

        return $"{QuoteIdentifier(aliases[node.Id])}.{QuoteIdentifier(column.EffectiveStorageName)}";
    }

    internal static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";

    internal static string QuoteStorageName(string value)
    {
        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            throw new ArgumentException("Storage name cannot be empty.", nameof(value));
        return string.Join(".", parts.Select(QuoteIdentifier));
    }

    private sealed record NodeOccurrence(ExecutionIRNode Node, int? ParentId);
}