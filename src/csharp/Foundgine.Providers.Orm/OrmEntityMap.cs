using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic.Query;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Orm;

/// <summary>
/// Rows fetched for one execution node, keyed by semantic field id. Contains the node's selected fields plus
/// any key/ordering fields the executor needed internally; the executor only ever emits the node's own fields.
/// </summary>
public sealed record OrmFetch(
    IReadOnlyList<IReadOnlyDictionary<FieldId, object?>> Rows,
    ExecutionPageInfo? PageInfo)
{
    /// <summary>Internal aggregate ordering values, aligned by row and COUNT-term order. These are never
    /// inserted into semantic field dictionaries or exposed as user-selected fields.</summary>
    internal IReadOnlyList<IReadOnlyList<object?>> AggregateCursorRows { get; init; } = [];
}

/// <summary>Non-generic face of a generated entity map, so the registry can dispatch without reflection.</summary>
public interface IOrmEntityMap
{
    string EntityName { get; }
    Type ClrType { get; }
    IReadOnlyCollection<string> FieldNames { get; }

    /// <summary>Resolves semantic ids from metadata. Throws when the generated map and metadata disagree.</summary>
    void Bind(EntityMetadata entity);

    void BindRelationship(RelationshipMetadata relationship, IOrmEntityMap target);

    void ValidateFilterExpression(SemanticFilterExpression filter);

    LambdaExpression LowerFilterExpression(SemanticFilterExpression filter, AuthorizationPredicate authorization, ExecutionContext context);
    LambdaExpression LowerUserFilterExpression(SemanticFilterExpression filter);
    LambdaExpression LowerAuthorizationExpression(AuthorizationPredicate authorization, ExecutionContext context);
    LambdaExpression AggregateSelector(FieldId field);

    string FieldName(FieldId field);
    void ValidateField(FieldId field);
    void ValidateAggregateField(FieldId field);

    /// <summary>The field backed by a physical column; used to resolve relationship keys and mutation columns.</summary>
    FieldId? FieldForColumn(ColumnId column);

    /// <summary>Node-local shape validation (children are validated by the caller). Throws
    /// <see cref="NotSupportedException"/> outside the generated capability set. Never touches the database.</summary>
    void Validate(ExecutionIRNode node, bool isRoot);

    /// <summary>
    /// Runs one node. <paramref name="keyField"/>/<paramref name="keys"/> restrict a child node to the given
    /// parent keys (applied after authorization, so they can only narrow). <paramref name="extraFields"/> are
    /// read in addition to the node's fields for joining.
    /// </summary>
    Task<OrmFetch> FetchAsync(
        ExecutionIRNode node,
        bool isRoot,
        IReadOnlyCollection<FieldId> extraFields,
        FieldId? keyField,
        IReadOnlyList<object?>? keys,
        IOrmQuerySource source,
        ExecutionContext context,
        CancellationToken cancellationToken);

    /// <summary>Shape validation of one mutation operation. Throws outside the capability set.</summary>
    void ValidateMutation(
        MutationKind kind,
        IReadOnlyList<FieldId> fields,
        SemanticFilterExpression? filter,
        IReadOnlyList<FieldId>? conflictFields,
        IReadOnlyList<FieldId>? returnFields);

    MutationResult ApplyMutation(
        MutationKind kind,
        IReadOnlyList<KeyValuePair<FieldId, object?>> values,
        SemanticFilterExpression? filter,
        IReadOnlyList<FieldId>? conflictFields,
        IReadOnlyList<FieldId>? returnFields,
        IOrmMutationSource source);
}

/// <summary>
/// Base class of every generated entity map. The generator supplies the typed field bindings; this class owns
/// the (hand-written, auditable) rules for composing them: authorization is always applied as its own Where
/// clause before any user filter, and only fields present in the authorized node are ever read back.
/// </summary>
public abstract class OrmEntityMap<TEntity> : IOrmEntityMap where TEntity : class
{
    private const int KeyChunkSize = 500;

    private readonly Dictionary<string, OrmFieldBinding<TEntity>> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<FieldId, OrmFieldBinding<TEntity>> _byId = new();
    private readonly Dictionary<ColumnId, FieldId> _byColumn = new();
    private readonly Dictionary<string, IOrmRelationshipBinding<TEntity>> _relationships = new(StringComparer.Ordinal);
    private readonly Dictionary<RelationshipId, IOrmRelationshipBinding<TEntity>> _relationshipsById = new();
    private readonly Dictionary<RelationshipId, IOrmEntityMap> _relationshipTargets = new();
    private readonly Func<TEntity>? _create;
    private EntityId? _entityId;

    protected OrmEntityMap(string entityName, Func<TEntity>? create = null)
    {
        EntityName = entityName;
        _create = create;
    }

    public string EntityName { get; }
    public Type ClrType => typeof(TEntity);
    public IReadOnlyCollection<string> FieldNames => _byName.Keys;

    protected void Add(OrmFieldBinding<TEntity> binding) => _byName.Add(binding.Name, binding);

    protected void AddRelationship(IOrmRelationshipBinding<TEntity> binding) => _relationships.Add(binding.Name, binding);

    public void BindRelationship(RelationshipMetadata relationship, IOrmEntityMap target)
    {
        if (relationship.Source != _entityId) return;
        if (!_relationships.TryGetValue(relationship.Name, out var binding)) return;
        if (!relationship.IsCollection)
            throw new InvalidOperationException($"Generated collection navigation '{EntityName}.{relationship.Name}' is bound to a non-collection semantic relationship.");
        if (binding.TargetType != target.ClrType)
            throw new InvalidOperationException(
                $"Generated navigation '{EntityName}.{relationship.Name}' targets '{binding.TargetType.Name}', " +
                $"but metadata relationship targets '{target.EntityName}' ({target.ClrType.Name}).");
        _relationshipsById[relationship.Id] = binding;
        _relationshipTargets[relationship.Id] = target;
    }

    public string FieldName(FieldId field) => Binding(field).Name;

    public void ValidateField(FieldId field) => _ = Binding(field);

    public void ValidateAggregateField(FieldId field)
    {
        var type = Nullable.GetUnderlyingType(Binding(field).ClrType) ?? Binding(field).ClrType;
        if (type != typeof(byte) && type != typeof(short) && type != typeof(int) && type != typeof(long) &&
            type != typeof(float) && type != typeof(double) && type != typeof(decimal))
            throw new NotSupportedException($"ORM provider: aggregate selector '{EntityName}.{Binding(field).Name}' must be a supported numeric field.");
    }

    public FieldId? FieldForColumn(ColumnId column) =>
        _byColumn.TryGetValue(column, out var field) ? field : null;

    public void ValidateFilterExpression(SemanticFilterExpression filter) => LowerFilter(filter, validateOnly: true);

    public LambdaExpression LowerFilterExpression(
        SemanticFilterExpression filter, AuthorizationPredicate authorization, ExecutionContext context)
    {
        var userPredicate = LowerFilter(filter, validateOnly: false)!;
        return ExpressionComposer.And(LowerAuthorization(authorization, context), userPredicate);
    }

    public LambdaExpression LowerUserFilterExpression(SemanticFilterExpression filter) => LowerFilter(filter, validateOnly: false)!;

    public LambdaExpression LowerAuthorizationExpression(AuthorizationPredicate authorization, ExecutionContext context) =>
        LowerAuthorization(authorization, context);

    public LambdaExpression AggregateSelector(FieldId field)
    {
        var selector = Binding(field).Selector;
        var body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert, Type: var boxed } conversion && boxed == typeof(object)
            ? conversion.Operand
            : selector.Body;
        return Expression.Lambda(body, selector.Parameters);
    }

    // ---------------------------------------------------------------- validation

    public void Validate(ExecutionIRNode node, bool isRoot)
    {
        EnsureBound();
        var expected = isRoot ? ExecutionOperation.Scan : ExecutionOperation.Traverse;
        if (node.Operation != expected)
            throw new NotSupportedException(
                $"ORM provider: operation '{node.Operation}' is not supported for a {(isRoot ? "root" : "child")} node.");
        if (node.ViaConnection is not null)
            throw new NotSupportedException("ORM provider: connections are not in the generated capability set.");
        if (!isRoot && node.ViaRelationship is null)
            throw new InvalidOperationException($"Child execution node {node.Id} has no relationship identity.");
        if (node.Fields.Count == 0)
            throw new InvalidOperationException(
                $"Execution node {node.Id} selects no fields after semantic authorization.");

        foreach (var field in node.Fields) Binding(field);

        var options = node.QueryOptions;
        if (options is not null)
        {
            if (options.Limit is < 0 || options.Offset is < 0)
                throw new InvalidOperationException("Query limit and offset must be non-negative.");

            if (!isRoot && (options.Limit is not null || options.Offset is not null || options.HasCursor))
                throw new NotSupportedException(
                    "ORM provider: per-parent child paging has no generated translation strategy. " +
                    "Declaring PerParentPagination in a capability profile does not install a translator.");

            foreach (var term in options.EffectiveOrder)
            {
                if (term.IsAggregate)
                {
                    ValidateAggregateOrder(term, node.Children);
                    if (options.HasCursor || options.Limit is not null || options.Offset is not null)
                        throw new NotSupportedException(
                            "ORM provider: aggregate ordering cannot yet be combined with limit, offset, or cursor pagination; " +
                            "aggregate keyset translation is implemented but remains disabled pending provider SQL conformance certification.");
                    continue;
                }
                if (!term.IsRootField)
                    throw new NotSupportedException(
                        "ORM provider: relationship-path ordering has no generated translation strategy.");
                Binding(term.Field);
            }

            if (options.HasCursor)
            {
                if (options.Offset is > 0)
                    throw new NotSupportedException("ORM provider: cursor paging cannot be combined with an offset.");
                // Fail closed at compile time when any key of the total order has no keyset operator.
                foreach (var key in OrderKeys(options.EffectiveOrder))
                {
                    var b = Binding(key.Field);
                    if (b.GreaterThan is null || b.LessThan is null)
                        throw new NotSupportedException(
                            $"ORM provider: field '{EntityName}.{b.Name}' ({b.ClrType.Name}) has no translatable " +
                            "ordering operator, so it cannot take part in cursor paging.");
                }
            }

            if (options.Filter is not null)
            {
                ValidateRelationshipAuthorization(options.Filter, node.Children);
                LowerFilter(options.Filter, validateOnly: true);
            }
        }

        if (node.Authorization is not null)
            LowerAuthorization(node.Authorization, context: null);
    }

    // ---------------------------------------------------------------- reads

    public async Task<OrmFetch> FetchAsync(
        ExecutionIRNode node,
        bool isRoot,
        IReadOnlyCollection<FieldId> extraFields,
        FieldId? keyField,
        IReadOnlyList<object?>? keys,
        IOrmQuerySource source,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        Validate(node, isRoot);
        var options = node.QueryOptions;
        var order = options?.EffectiveOrder ?? [];

        var limit = isRoot ? options?.Limit : null;
        var offset = isRoot ? options?.Offset : null;
        if (isRoot)
        {
            if (context.TryGetValue(ExecutionContextKeys.PaginationLimit, out var runtimeLimit) && runtimeLimit is not null)
                limit = Convert.ToInt32(runtimeLimit, CultureInfo.InvariantCulture);
            if (context.TryGetValue(ExecutionContextKeys.PaginationOffset, out var runtimeOffset) && runtimeOffset is not null)
                offset = Convert.ToInt32(runtimeOffset, CultureInfo.InvariantCulture);
        }

        var cursor = isRoot && options is { HasCursor: true } ? options.After : null;
        var paged = limit is > 0 || offset is > 0 || cursor is not null;
        if (paged && order.Any(x => x.IsAggregate))
            throw new NotSupportedException(
                "ORM provider: aggregate ordering cannot be combined with runtime pagination; aggregate keyset translation is implemented but remains disabled pending provider SQL conformance certification.");
        var orderKeys = paged ? OrderKeys(order) : order;

        // Everything read from an entity: the node's fields, join keys, and (for cursors) the ordering tuple.
        var read = new List<FieldId>(node.Fields);
        foreach (var f in extraFields) if (!read.Contains(f)) read.Add(f);
        if (cursor is not null || (limit is > 0 && offset is null))
            foreach (var t in orderKeys) if (!read.Contains(t.Field)) read.Add(t.Field);

        Expression<Func<TEntity, bool>>? keyset = null;
        if (cursor is not null)
        {
            var decoded = orderKeys.Any(x => x.IsAggregate && x.Aggregate == SemanticOrderAggregate.Count)
                ? OrmCursor.DecodeWithInt32Components(cursor, CountCursorComponentIndexes(orderKeys))
                : OrmCursor.Decode(cursor);
            keyset = BuildKeyset(orderKeys, decoded, node.Children, context);
        }

        // Forward pagination (limit without offset, or any cursor page) fetches one extra row for HasNextPage.
        var forward = limit is > 0 && offset is null;
        var chunks = keys is null ? [null] : Chunk(keys);
        var rows = new List<IReadOnlyDictionary<FieldId, object?>>();
        var aggregateCursorRows = new List<IReadOnlyList<object?>>();
        var hasNext = false;

        // Compose a provider-translatable projection from generated field selectors. This deliberately
        // materializes object arrays rather than entities: EF Core selects only the requested
        // columns (plus explicitly required internal keys), while the generated selectors keep member
        // access statically known and avoid Expression.Compile()/runtime DTO generation.
        var countCursorSelectors = BuildCountCursorSelectors(order, node.Children, context);
        var projectionParameter = Expression.Parameter(typeof(TEntity), "entity");
        var projectionValues = read.Select(fieldId => (Expression)Expression.Convert(
            ReplaceParameter(Binding(fieldId).Selector.Body, Binding(fieldId).Selector.Parameters[0], projectionParameter),
            typeof(object))).ToList();
        // COUNT selectors are appended as private projection slots. They use the same authorized
        // correlated selector as ORDER BY and are deliberately kept outside FieldId dictionaries.
        foreach (var selector in countCursorSelectors)
            projectionValues.Add(Expression.Convert(
                ReplaceParameter(selector.Body, selector.Parameters[0], projectionParameter), typeof(object)));
        var projectionBody = Expression.NewArrayInit(typeof(object), projectionValues);
        var projection = Expression.Lambda<Func<TEntity, object?[]>>(projectionBody, projectionParameter);

        foreach (var chunk in chunks)
        {
            IQueryable<TEntity> query = source.Query<TEntity>();

            // Authorization first and as its own clause: later composition can only narrow, never replace it.
            if (node.Authorization is not null)
                query = query.Where(LowerAuthorization(node.Authorization, context));
            if (chunk is not null)
                query = query.Where(Binding(keyField!.Value).In(chunk));
            if (options?.Filter is not null)
            {
                var relatedAuthorization = node.Children
                    .Where(child => child.ViaRelationship is not null && child.Authorization is not null)
                    .GroupBy(child => child.ViaRelationship!.Value)
                    .ToDictionary(group => group.Key, group => group.Select(child => child.Authorization!).Distinct().Single());
                query = query.Where(LowerFilter(options.Filter, validateOnly: false, context, relatedAuthorization)!);
            }
            if (keyset is not null)
                query = query.Where(keyset);

            query = ApplyOrder(query, orderKeys, paged, node.Children, context);
            if (offset is > 0) query = query.Skip(offset.Value);
            if (limit is > 0) query = query.Take(forward ? limit.Value + 1 : limit.Value);

            var projected = await source.ToListAsync(query.Select(projection), cancellationToken).ConfigureAwait(false);
            var count = projected.Count;
            if (forward && count > limit!.Value)
            {
                hasNext = true;
                count = limit.Value;
            }

            for (var i = 0; i < count; i++)
            {
                var values = new Dictionary<FieldId, object?>(read.Count);
                for (var fieldIndex = 0; fieldIndex < read.Count; fieldIndex++)
                    values[read[fieldIndex]] = projected[i][fieldIndex];
                rows.Add(values);
                if (countCursorSelectors.Count > 0)
                    aggregateCursorRows.Add(Enumerable.Range(read.Count, countCursorSelectors.Count)
                        .Select(slot => projected[i][slot]).ToArray());
            }
        }

        ExecutionPageInfo? page = null;
        if (forward || cursor is not null)
        {
            string? start = null, end = null;
            if (rows.Count > 0)
            {
                start = EncodeCursorTuple(orderKeys, rows[0], aggregateCursorRows.Count > 0 ? aggregateCursorRows[0] : []);
                end = EncodeCursorTuple(orderKeys, rows[^1], aggregateCursorRows.Count > 0 ? aggregateCursorRows[^1] : []);
            }

            page = new ExecutionPageInfo(start, end, hasNext, HasPreviousPage: cursor is not null);
        }

        return new OrmFetch(rows, page) { AggregateCursorRows = aggregateCursorRows };
    }

    // Builds the complete mixed tuple in semantic order. COUNT values come from private projection
    // slots; ordinary fields come from the generated field dictionary. MIN/MAX remain unsupported here.
    private static string EncodeCursorTuple(
        IReadOnlyList<SemanticOrderTerm> orderKeys,
        IReadOnlyDictionary<FieldId, object?> row,
        IReadOnlyList<object?> aggregateValues)
    {
        var tuple = new object?[orderKeys.Count];
        var aggregateIndex = 0;
        var countIndexes = new List<int>();
        for (var i = 0; i < orderKeys.Count; i++)
        {
            var term = orderKeys[i];
            if (!term.IsAggregate)
            {
                tuple[i] = row[term.Field];
                continue;
            }
            if (term.Aggregate != SemanticOrderAggregate.Count)
                throw new NotSupportedException("ORM provider: only COUNT has a typed aggregate cursor tuple representation.");
            if ((uint)aggregateIndex >= (uint)aggregateValues.Count || aggregateValues[aggregateIndex] is not int)
                throw new InvalidOperationException("ORM provider: the projected COUNT cursor slot is missing or is not Int32.");
            tuple[i] = aggregateValues[aggregateIndex++];
            countIndexes.Add(i);
        }
        if (aggregateIndex != aggregateValues.Count)
            throw new InvalidOperationException("ORM provider: aggregate cursor projection slots do not match the ordering tuple.");
        return OrmCursor.EncodeWithInt32Components(tuple, countIndexes);
    }

    private static IReadOnlyList<int> CountCursorComponentIndexes(IReadOnlyList<SemanticOrderTerm> orderKeys) =>
        orderKeys.Select((term, index) => (term, index))
            .Where(x => x.term.IsAggregate && x.term.Aggregate == SemanticOrderAggregate.Count)
            .Select(x => x.index)
            .ToArray();

    private IReadOnlyList<LambdaExpression> BuildCountCursorSelectors(
        IReadOnlyList<SemanticOrderTerm> terms,
        IReadOnlyList<ExecutionIRNode> children,
        ExecutionContext context)
    {
        var selectors = new List<LambdaExpression>();
        foreach (var term in terms.Where(x => x.IsAggregate && x.Aggregate == SemanticOrderAggregate.Count))
        {
            if (term.EffectivePath.Count != 1 || !_relationshipsById.TryGetValue(term.EffectivePath[0], out var navigation))
                throw new NotSupportedException("ORM provider: COUNT cursor projection requires one generated collection navigation.");
            var child = children.Where(x => x.ViaRelationship == term.EffectivePath[0]).ToArray();
            if (child.Length == 0 || child.Any(x => x.Authorization is null))
                throw new InvalidOperationException("ORM provider: COUNT cursor projection requires an authorized child execution node.");
            var authorizations = child.Select(x => x.Authorization!).Distinct().ToArray();
            if (authorizations.Length != 1)
                throw new InvalidOperationException("ORM provider: conflicting child authorization predicates cannot be used for COUNT cursor projection.");
            var targetMap = _relationshipTargets[term.EffectivePath[0]];
            var predicate = targetMap.LowerAuthorizationExpression(authorizations[0], context);
            selectors.Add(navigation.BuildCountAggregateOrderSelector(predicate));
        }
        return selectors;
    }

    private static Expression ReplaceParameter(Expression body, ParameterExpression source, ParameterExpression target) =>
        new ParameterReplaceVisitor(source, target).Visit(body)!;

    private sealed class ParameterReplaceVisitor(ParameterExpression source, ParameterExpression target)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == source ? target : base.VisitParameter(node);
    }

    private static IEnumerable<IReadOnlyList<object?>> Chunk(IReadOnlyList<object?> keys)
    {
        for (var i = 0; i < keys.Count; i += KeyChunkSize)
            yield return keys.Skip(i).Take(KeyChunkSize).ToArray();
    }

    /// <summary>The total order used for paging: requested terms, then primary key(s) not already present.</summary>
    private IReadOnlyList<SemanticOrderTerm> OrderKeys(IReadOnlyList<SemanticOrderTerm> terms)
    {
        var result = new List<SemanticOrderTerm>(terms);
        foreach (var pair in _byId)
        {
            if (!pair.Value.IsPrimaryKey) continue;
            if (result.Any(t => t.Field == pair.Key)) continue;
            result.Add(new SemanticOrderTerm(pair.Key, SemanticSortDirection.Asc));
        }

        return result;
    }

    private IQueryable<TEntity> ApplyOrder(
        IQueryable<TEntity> query,
        IReadOnlyList<SemanticOrderTerm> terms,
        bool requireDeterministic,
        IReadOnlyList<ExecutionIRNode> children,
        ExecutionContext context)
    {
        IOrderedQueryable<TEntity>? ordered = null;
        foreach (var term in terms)
        {
            var desc = term.Direction == SemanticSortDirection.Desc;
            if (term.IsAggregate)
            {
                if (term.EffectivePath.Count != 1 || !_relationshipsById.TryGetValue(term.EffectivePath[0], out var navigation) ||
                    !_relationshipTargets.TryGetValue(term.EffectivePath[0], out var targetMap))
                    throw new NotSupportedException("ORM provider: aggregate ordering requires one generated collection navigation.");
                var child = children.Where(x => x.ViaRelationship == term.EffectivePath[0]).ToArray();
                if (child.Length == 0 || child.Any(x => x.Authorization is null))
                    throw new InvalidOperationException(
                        $"ORM provider: aggregate ordering on relationship '{term.EffectivePath[0].Value}' requires an authorized child execution node.");
                var authorizations = child.Select(x => x.Authorization!).Distinct().ToArray();
                if (authorizations.Length != 1)
                    throw new InvalidOperationException("ORM provider: conflicting child authorization predicates cannot be used for aggregate ordering.");
                var predicate = targetMap.LowerAuthorizationExpression(authorizations[0], context);
                var selector = term.Aggregate == SemanticOrderAggregate.Count ? null : targetMap.AggregateSelector(term.Field);
                ordered = navigation.ApplyAggregateOrder(ordered ?? query, predicate, term.Aggregate, selector, desc, ordered is not null);
                continue;
            }

            var binding = Binding(term.Field);
            ordered = ordered is null ? binding.OrderBy(query, desc) : binding.ThenBy(ordered, desc);
        }

        if (terms.Any(t => t.IsAggregate) && !terms.Any(t => !t.IsAggregate && Binding(t.Field).IsPrimaryKey))
        {
            var primaryKey = _byId.FirstOrDefault(pair => pair.Value.IsPrimaryKey);
            if (primaryKey.Value is null)
                throw new InvalidOperationException(
                    $"Aggregate ordering '{EntityName}' needs a primary key for deterministic tie-breaking.");
            ordered = ordered is null
                ? primaryKey.Value.OrderBy(query, descending: false)
                : primaryKey.Value.ThenBy(ordered, descending: false);
        }

        if (requireDeterministic && !terms.Any(t => !t.IsAggregate && Binding(t.Field).IsPrimaryKey))
            throw new InvalidOperationException(
                $"Paging '{EntityName}' needs a deterministic order but no primary key is declared.");

        return ordered ?? query;
    }

    private void ValidateAggregateOrder(SemanticOrderTerm term, IReadOnlyList<ExecutionIRNode> children)
    {
        if (term.Aggregate is not (SemanticOrderAggregate.Count or SemanticOrderAggregate.Min or SemanticOrderAggregate.Max))
            throw new NotSupportedException($"ORM provider: aggregate ordering '{term.Aggregate}' is not supported.");
        if (term.EffectivePath.Count != 1 || !_relationshipsById.ContainsKey(term.EffectivePath[0]) ||
            !_relationshipTargets.TryGetValue(term.EffectivePath[0], out var targetMap))
            throw new NotSupportedException("ORM provider: aggregate ordering requires exactly one generated collection relationship.");
        if (term.Aggregate is SemanticOrderAggregate.Min or SemanticOrderAggregate.Max)
            targetMap.ValidateAggregateField(term.Field);
        var matching = children.Where(x => x.ViaRelationship == term.EffectivePath[0]).ToArray();
        if (matching.Length == 0 || matching.Any(x => x.Authorization is null))
            throw new InvalidOperationException(
                $"ORM provider: {term.Aggregate} ordering on relationship '{term.EffectivePath[0].Value}' requires a matching child execution node with authorization.");
        if (matching.Select(x => x.Authorization!).Distinct().Count() != 1)
            throw new InvalidOperationException("ORM provider: conflicting child authorization predicates cannot be used for aggregate ordering.");
    }

    /// <summary>
    /// Keyset predicate for "rows strictly after the cursor" under mixed ascending/descending terms:
    /// (k1 ? v1) OR (k1 = v1 AND k2 ? v2) OR ... where ? is &gt; for ascending and &lt; for descending.
    /// Null ordering values are rejected: their relative position is provider-specific, so cursors over
    /// nullable data fail closed rather than silently skipping or repeating rows.
    /// </summary>
    private Expression<Func<TEntity, bool>> BuildKeyset(
        IReadOnlyList<SemanticOrderTerm> terms,
        IReadOnlyList<string?> cursorValues,
        IReadOnlyList<ExecutionIRNode> children,
        ExecutionContext context)
    {
        if (cursorValues.Count != terms.Count)
            throw new InvalidOperationException("The pagination cursor does not match the ordering of this query.");

        Expression<Func<TEntity, bool>>? result = null;
        Expression<Func<TEntity, bool>>? prefix = null;
        for (var i = 0; i < terms.Count; i++)
        {
            var term = terms[i];
            var value = cursorValues[i];
            if (value is null)
                throw new NotSupportedException("ORM provider: cursor paging cannot position on a null ordering value.");

            Expression<Func<TEntity, bool>> comparison;
            Expression<Func<TEntity, bool>> equality;
            if (term.IsAggregate)
            {
                if (term.Aggregate != SemanticOrderAggregate.Count)
                    throw new NotSupportedException("ORM provider: only COUNT aggregate cursor comparisons are supported.");
                var count = OrmCursor.DecodeInt32(cursorValues, i);
                var (navigation, predicate) = ResolveAuthorizedCountPredicate(term, children, context);
                comparison = (Expression<Func<TEntity, bool>>)navigation.BuildCountAggregateCursorComparison(
                    predicate, count, term.Direction == SemanticSortDirection.Desc, equal: false);
                equality = (Expression<Func<TEntity, bool>>)navigation.BuildCountAggregateCursorComparison(
                    predicate, count, term.Direction == SemanticSortDirection.Desc, equal: true);
            }
            else
            {
                var binding = Binding(term.Field);
                var descending = term.Direction == SemanticSortDirection.Desc;
                comparison = (descending ? binding.LessThan : binding.GreaterThan)!(value);
                equality = binding.Equal(value);
            }

            var branch = prefix is null ? comparison : ExpressionComposer.And(prefix, comparison);
            result = result is null ? branch : ExpressionComposer.Or(result, branch);
            prefix = prefix is null ? equality : ExpressionComposer.And(prefix, equality);
        }

        return result ?? ExpressionComposer.Constant<TEntity>(true);
    }

    private (IOrmRelationshipBinding<TEntity> Navigation, LambdaExpression Predicate) ResolveAuthorizedCountPredicate(
        SemanticOrderTerm term,
        IReadOnlyList<ExecutionIRNode> children,
        ExecutionContext context)
    {
        if (term.EffectivePath.Count != 1 ||
            !_relationshipsById.TryGetValue(term.EffectivePath[0], out var navigation) ||
            !_relationshipTargets.TryGetValue(term.EffectivePath[0], out var targetMap))
            throw new NotSupportedException("ORM provider: COUNT cursor comparison requires one generated collection relationship.");

        var matching = children.Where(x => x.ViaRelationship == term.EffectivePath[0]).ToArray();
        if (matching.Length == 0 || matching.Any(x => x.Authorization is null))
            throw new InvalidOperationException("ORM provider: COUNT cursor comparison requires an authorized child execution node.");
        var authorizations = matching.Select(x => x.Authorization!).Distinct().ToArray();
        if (authorizations.Length != 1)
            throw new InvalidOperationException("ORM provider: conflicting child authorization predicates cannot be used for COUNT cursor comparison.");

        return (navigation, targetMap.LowerAuthorizationExpression(authorizations[0], context));
    }

    // ---------------------------------------------------------------- mutations

    public void ValidateMutation(
        MutationKind kind,
        IReadOnlyList<FieldId> fields,
        SemanticFilterExpression? filter,
        IReadOnlyList<FieldId>? conflictFields,
        IReadOnlyList<FieldId>? returnFields)
    {
        EnsureBound();
        if (_create is null && kind is MutationKind.Create or MutationKind.Upsert)
            throw new NotSupportedException(
                $"ORM provider: entity '{EntityName}' has no public parameterless constructor for creation.");

        if (kind is MutationKind.Create or MutationKind.Update or MutationKind.Upsert && fields.Count == 0)
            throw new InvalidOperationException($"Mutation of '{EntityName}' sets no fields.");

        foreach (var field in fields)
        {
            if (Binding(field).Set is null)
                throw new NotSupportedException(
                    $"ORM provider: field '{EntityName}.{Binding(field).Name}' is read-only and cannot be mutated.");
        }

        foreach (var field in returnFields ?? []) Binding(field);

        if (kind is MutationKind.Update or MutationKind.Delete && filter is null)
            throw new InvalidOperationException(
                $"{kind} of '{EntityName}' has no filter; the ORM provider refuses unfiltered updates and deletes.");
        if (kind is MutationKind.Create && filter is not null)
            throw new NotSupportedException("ORM provider: a create operation cannot carry a filter.");
        if (filter is not null && ContainsRelationshipFilter(filter))
            throw new NotSupportedException("ORM provider: relationship-filtered mutations are not supported.");
        if (filter is not null) LowerFilter(filter, validateOnly: true);

        if (kind == MutationKind.Upsert)
        {
            if (conflictFields is null || conflictFields.Count == 0)
                throw new InvalidOperationException($"Upsert of '{EntityName}' declares no conflict columns.");
            foreach (var c in conflictFields)
            {
                Binding(c);
                if (!fields.Contains(c))
                    throw new InvalidOperationException(
                        $"Upsert conflict field '{EntityName}.{Binding(c).Name}' is not among the values written.");
            }
        }
    }

    public MutationResult ApplyMutation(
        MutationKind kind,
        IReadOnlyList<KeyValuePair<FieldId, object?>> values,
        SemanticFilterExpression? filter,
        IReadOnlyList<FieldId>? conflictFields,
        IReadOnlyList<FieldId>? returnFields,
        IOrmMutationSource source)
    {
        ValidateMutation(kind, values.Select(x => x.Key).ToArray(), filter, conflictFields, returnFields);

        switch (kind)
        {
            case MutationKind.Create:
                return CreateOne(values, returnFields, source);

            case MutationKind.Update:
            {
                var matched = source.TrackedQuery<TEntity>().Where(LowerFilter(filter!, validateOnly: false)!).ToList();
                foreach (var entity in matched) SetValues(entity, values);
                if (matched.Count > 0) source.SaveChanges();
                return new MutationResult(matched.Count, Returned(matched.FirstOrDefault(), returnFields));
            }

            case MutationKind.Delete:
            {
                var matched = source.TrackedQuery<TEntity>().Where(LowerFilter(filter!, validateOnly: false)!).ToList();
                var returned = Returned(matched.FirstOrDefault(), returnFields); // read before the row is gone
                foreach (var entity in matched) source.Remove(entity);
                if (matched.Count > 0) source.SaveChanges();
                return new MutationResult(matched.Count, returned);
            }

            case MutationKind.Upsert:
            {
                Expression<Func<TEntity, bool>> match = ExpressionComposer.Constant<TEntity>(true);
                foreach (var conflict in conflictFields!)
                    match = ExpressionComposer.And(match, Binding(conflict).Equal(values.First(v => v.Key == conflict).Value));
                if (filter is not null)
                    match = ExpressionComposer.And(match, LowerFilter(filter, validateOnly: false)!);

                var existing = source.TrackedQuery<TEntity>().Where(match).ToList();
                if (existing.Count == 0) return CreateOne(values, returnFields, source);
                if (existing.Count > 1)
                    throw new InvalidOperationException(
                        $"Upsert of '{EntityName}' matched {existing.Count} rows; the conflict columns are not unique.");

                SetValues(existing[0], values);
                source.SaveChanges();
                return new MutationResult(1, Returned(existing[0], returnFields));
            }

            default:
                throw new NotSupportedException($"ORM provider: mutation kind '{kind}' is not supported.");
        }
    }

    private MutationResult CreateOne(
        IReadOnlyList<KeyValuePair<FieldId, object?>> values,
        IReadOnlyList<FieldId>? returnFields,
        IOrmMutationSource source)
    {
        var entity = _create!();
        SetValues(entity, values);
        source.Add(entity);
        source.SaveChanges(); // database-generated keys are populated here, for later operations to reference
        return new MutationResult(1, Returned(entity, returnFields));
    }

    private void SetValues(TEntity entity, IReadOnlyList<KeyValuePair<FieldId, object?>> values)
    {
        foreach (var (field, value) in values) Binding(field).Set!(entity, value);
    }

    private IReadOnlyDictionary<FieldId, object?>? Returned(TEntity? entity, IReadOnlyList<FieldId>? fields)
    {
        if (fields is null || fields.Count == 0 || entity is null) return null;
        var result = new Dictionary<FieldId, object?>(fields.Count);
        foreach (var f in fields) result[f] = Binding(f).Get(entity);
        return result;
    }

    // ---------------------------------------------------------------- filters

    private Expression<Func<TEntity, bool>>? LowerFilter(
        SemanticFilterExpression filter,
        bool validateOnly,
        ExecutionContext? context = null,
        IReadOnlyDictionary<RelationshipId, AuthorizationPredicate>? relationshipAuthorization = null)
    {
        switch (filter)
        {
            case SemanticFieldFilter f:
            {
                var binding = Binding(f.Field);
                if (validateOnly) return null;
                return f.Operator switch
                {
                    SemanticFilterOperator.Eq => binding.Equal(f.Value),
                    SemanticFilterOperator.Neq => binding.NotEqual(f.Value),
                    SemanticFilterOperator.In => binding.In(ToList(f.Value)),
                    _ => throw new NotSupportedException($"Filter operator '{f.Operator}' is not supported.")
                };
            }
            case SemanticAndFilter and:
                return Fold(and.Expressions, ExpressionComposer.And, true, validateOnly, context, relationshipAuthorization);
            case SemanticOrFilter or:
                return Fold(or.Expressions, ExpressionComposer.Or, false, validateOnly, context, relationshipAuthorization);
            case SemanticRelationshipFilter relationship:
            {
                if (!_relationshipsById.TryGetValue(relationship.Relationship, out var navigation) ||
                    !_relationshipTargets.TryGetValue(relationship.Relationship, out var target))
                    throw new NotSupportedException($"ORM provider: relationship '{relationship.Relationship.Value}' has no generated collection-navigation binding.");
                if (ContainsRelationshipFilter(relationship.Predicate))
                    throw new NotSupportedException("ORM provider: nested relationship filters require a separately authorized nested execution node and are not yet supported.");
                target.ValidateFilterExpression(relationship.Predicate);
                if (validateOnly) return null;
                if (relationshipAuthorization is null ||
                    !relationshipAuthorization.TryGetValue(relationship.Relationship, out var authorization) || context is null)
                    throw new NotSupportedException("ORM provider: relationship filters require an authorization predicate on the matching child ExecutionIR node.");
                var child = target.LowerFilterExpression(relationship.Predicate, authorization, context);
                return (Expression<Func<TEntity, bool>>)navigation.Build(child, relationship.Quantifier);
            }
            case SemanticAggregateFilter aggregate:
            {
                if (!_relationshipsById.TryGetValue(aggregate.Relationship, out var navigation) ||
                    !_relationshipTargets.TryGetValue(aggregate.Relationship, out var target))
                    throw new NotSupportedException($"ORM provider: aggregate relationship '{aggregate.Relationship.Value}' has no generated collection-navigation binding.");
                if (validateOnly)
                {
                    if (aggregate.Aggregate is not (SemanticFilterAggregate.Count or SemanticFilterAggregate.Min or SemanticFilterAggregate.Max))
                        throw new NotSupportedException($"Aggregate '{aggregate.Aggregate}' is not supported.");
                    if (aggregate.Aggregate != SemanticFilterAggregate.Count && aggregate.Field is null)
                        throw new NotSupportedException($"Aggregate '{aggregate.Aggregate}' requires a target field.");
                    if (aggregate.Value is null)
                        throw new NotSupportedException("Aggregate comparisons require a non-null comparison value.");
                    if (aggregate.Field is { } aggregateField) target.ValidateAggregateField(aggregateField);
                    if (aggregate.Predicate is not null) target.ValidateFilterExpression(aggregate.Predicate);
                    return null;
                }
                if (relationshipAuthorization is null ||
                    !relationshipAuthorization.TryGetValue(aggregate.Relationship, out var authorization) || context is null)
                    throw new NotSupportedException("ORM provider: aggregate filters require an authorization predicate on the matching child ExecutionIR node.");
                if (aggregate.Predicate is not null && ContainsRelationshipFilter(aggregate.Predicate))
                    throw new NotSupportedException("ORM provider: nested relationship predicates inside aggregates are not supported.");
                var auth = target.LowerAuthorizationExpression(authorization, context);
                var childPredicate = aggregate.Predicate is null
                    ? auth
                    : ExpressionComposer.And(auth, target.LowerUserFilterExpression(aggregate.Predicate));
                var selector = aggregate.Field is { } field ? target.AggregateSelector(field) : null;
                return (Expression<Func<TEntity, bool>>)navigation.BuildAggregate(childPredicate, aggregate.Aggregate, selector, aggregate.Operator, aggregate.Value);
            }
            default:
                throw new NotSupportedException($"Filter '{filter.GetType().Name}' is not supported.");
        }
    }

    private Expression<Func<TEntity, bool>>? Fold(
        IReadOnlyList<SemanticFilterExpression> expressions,
        Func<Expression<Func<TEntity, bool>>, Expression<Func<TEntity, bool>>, Expression<Func<TEntity, bool>>> combine,
        bool identity,
        bool validateOnly,
        ExecutionContext? context = null,
        IReadOnlyDictionary<RelationshipId, AuthorizationPredicate>? relationshipAuthorization = null)
    {
        Expression<Func<TEntity, bool>>? result = null;
        foreach (var expression in expressions)
        {
            var next = LowerFilter(expression, validateOnly, context, relationshipAuthorization);
            if (validateOnly) continue;
            result = result is null ? next : combine(result, next!);
        }

        return validateOnly ? null : result ?? ExpressionComposer.Constant<TEntity>(identity);
    }

    private static bool ContainsRelationshipFilter(SemanticFilterExpression filter) => filter switch
    {
        SemanticRelationshipFilter => true,
        SemanticAndFilter and => and.Expressions.Any(ContainsRelationshipFilter),
        SemanticOrFilter or => or.Expressions.Any(ContainsRelationshipFilter),
        _ => false
    };

    private static void ValidateRelationshipAuthorization(
        SemanticFilterExpression filter,
        IReadOnlyList<ExecutionIRNode> children)
    {
        switch (filter)
        {
            case SemanticRelationshipFilter relationship:
            {
                var matching = children.Where(child => child.ViaRelationship == relationship.Relationship).ToArray();
                if (matching.Length == 0 || matching.Any(child => child.Authorization is null))
                    throw new NotSupportedException(
                        $"ORM provider: relationship filter '{relationship.Relationship.Value}' requires matching child execution nodes, each with an authorization predicate.");
                if (matching.Select(child => child.Authorization!).Distinct().Count() != 1)
                    throw new NotSupportedException(
                        $"ORM provider: relationship filter '{relationship.Relationship.Value}' has conflicting child authorization predicates.");
                if (ContainsRelationshipFilter(relationship.Predicate))
                    throw new NotSupportedException("ORM provider: nested relationship filters require a separately authorized nested execution node and are not yet supported.");
                break;
            }
            case SemanticAggregateFilter aggregate:
            {
                var matching = children.Where(child => child.ViaRelationship == aggregate.Relationship).ToArray();
                if (matching.Length == 0 || matching.Any(child => child.Authorization is null) ||
                    matching.Select(child => child.Authorization!).Distinct().Count() != 1)
                    throw new NotSupportedException($"ORM provider: aggregate filter '{aggregate.Relationship.Value}' requires one consistent authorized child execution node.");
                break;
            }
            case SemanticAndFilter and:
                foreach (var child in and.Expressions) ValidateRelationshipAuthorization(child, children);
                break;
            case SemanticOrFilter or:
                foreach (var child in or.Expressions) ValidateRelationshipAuthorization(child, children);
                break;
        }
    }

    private static IReadOnlyList<object?> ToList(object? value)
    {
        if (value is null || value is string || value is not IEnumerable sequence)
            throw new InvalidOperationException("The 'in' operator requires a collection value.");
        var list = new List<object?>();
        foreach (var item in sequence) list.Add(item);
        return list;
    }

    // ---------------------------------------------------------------- authorization

    /// <summary>Lowers the provider-neutral authorization predicate to a typed expression. Context values are
    /// resolved now (per execution), never baked into a cached plan. Unknown shapes fail closed.</summary>
    private Expression<Func<TEntity, bool>> LowerAuthorization(AuthorizationPredicate p, ExecutionContext? context)
    {
        switch (p.Kind)
        {
            case AuthorizationPredicateKind.And:
                return ExpressionComposer.And(LowerAuthorization(Req(p.Left), context), LowerAuthorization(Req(p.Right), context));
            case AuthorizationPredicateKind.Or:
                return ExpressionComposer.Or(LowerAuthorization(Req(p.Left), context), LowerAuthorization(Req(p.Right), context));
            case AuthorizationPredicateKind.Not:
                return ExpressionComposer.Not(LowerAuthorization(Req(p.Left), context));
            case AuthorizationPredicateKind.Constant:
                return ExpressionComposer.Constant<TEntity>(OrmValue.ParseConstant(p.Value) is true);
            case AuthorizationPredicateKind.MemberAccess when IsResourceMember(p):
            {
                var binding = ResourceBinding(p);
                if (binding.ClrType != typeof(bool))
                    throw new NotSupportedException(
                        $"Authorization member '{p.Name}' is used as a predicate but is not boolean.");
                return binding.Equal(true);
            }
            case AuthorizationPredicateKind.Equal:
            case AuthorizationPredicateKind.NotEqual:
                return LowerComparison(p, context);
            default:
                throw new NotSupportedException(
                    $"Authorization predicate '{p.Kind}' is not supported by the ORM provider.");
        }
    }

    private Expression<Func<TEntity, bool>> LowerComparison(AuthorizationPredicate p, ExecutionContext? context)
    {
        var negate = p.Kind == AuthorizationPredicateKind.NotEqual;
        var left = Req(p.Left);
        var right = Req(p.Right);
        var leftIsResource = IsResourceMember(left);
        var rightIsResource = IsResourceMember(right);

        if (leftIsResource && rightIsResource)
            throw new NotSupportedException("Authorization comparisons between two resource members are not supported.");

        if (leftIsResource || rightIsResource)
        {
            var binding = ResourceBinding(leftIsResource ? left : right);
            var valueNode = leftIsResource ? right : left;
            // During validation (no context) only the shape is checked.
            var value = context is null ? null : ResolveValue(valueNode, context);
            if (context is null) { ValidateValueNode(valueNode); return ExpressionComposer.Constant<TEntity>(false); }
            return negate ? binding.NotEqual(value) : binding.Equal(value);
        }

        if (context is null)
        {
            ValidateValueNode(left);
            ValidateValueNode(right);
            return ExpressionComposer.Constant<TEntity>(false);
        }

        var equal = Equals(ResolveValue(left, context), ResolveValue(right, context));
        return ExpressionComposer.Constant<TEntity>(negate ? !equal : equal);
    }

    private static void ValidateValueNode(AuthorizationPredicate node)
    {
        if (node.Kind == AuthorizationPredicateKind.Constant) return;
        if (node.Kind == AuthorizationPredicateKind.MemberAccess && node.Left?.Kind == AuthorizationPredicateKind.ContextParameter) return;
        throw new NotSupportedException(
            "Only direct resource and context member access, and constants, are supported in authorization.");
    }

    private static object? ResolveValue(AuthorizationPredicate node, ExecutionContext context)
    {
        if (node.Kind == AuthorizationPredicateKind.Constant) return OrmValue.ParseConstant(node.Value);
        ValidateValueNode(node);
        var path = node.Left!.Name + "." + node.Name;
        if (!context.TryGetValue(path, out var value))
            throw new InvalidOperationException($"Execution context does not contain authorization value '{path}'.");
        return value;
    }

    private static bool IsResourceMember(AuthorizationPredicate p) =>
        p.Kind == AuthorizationPredicateKind.MemberAccess && p.Left?.Kind == AuthorizationPredicateKind.ResourceParameter;

    private OrmFieldBinding<TEntity> ResourceBinding(AuthorizationPredicate member) =>
        _byName.TryGetValue(member.Name ?? "", out var binding)
            ? binding
            : throw new InvalidOperationException(
                $"Authorization resource member '{EntityName}.{member.Name}' has no generated ORM binding.");

    private static AuthorizationPredicate Req(AuthorizationPredicate? p) =>
        p ?? throw new InvalidOperationException("Malformed authorization predicate.");

    private OrmFieldBinding<TEntity> Binding(FieldId id)
    {
        EnsureBound();
        return _byId.TryGetValue(id, out var binding)
            ? binding
            : throw new InvalidOperationException($"Field '{id}' is not mapped for entity '{EntityName}'.");
    }

    private void EnsureBound()
    {
        if (_entityId is null)
            throw new InvalidOperationException($"ORM map '{EntityName}' has not been bound to metadata.");
    }
}
