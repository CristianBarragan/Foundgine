using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.IR;

/// <summary>
/// Fluent builder for semantic reads. It only assembles the core IR (<see cref="SemanticOperation"/>); authorization,
/// planning and execution are unchanged. Defaults: all fields of the entity; an included relationship resolves its
/// target entity from the metadata and selects all of its fields. Fields may be given as ids or by name
/// (case-insensitive). The Java twin is <c>com.foundgine.core.semantic.ir.SemanticQuery</c> with the same members
/// in camelCase.
/// </summary>
/// <example>
/// <code>
/// var op = SemanticQuery.From(metadata, "Warehouse")
///     .Select("Id", "Name")
///     .WhereEq("Region", "NZ")
///     .Include(inventoryRelationship, lots => lots.Select("Sku", "OnHand"))
///     .Build();
/// </code>
/// </example>
public sealed class SemanticQuery
{
    private readonly MetadataRegistry _metadata;
    private readonly EntityMetadata _entity;
    private readonly RelationshipId? _via;
    private readonly List<SemanticFilterExpression> _filters = [];
    private readonly List<SemanticOrderTerm> _order = [];
    private readonly List<SemanticQuery> _includes = [];
    private IReadOnlyList<FieldId>? _fields;
    private int? _limit, _offset;

    private SemanticQuery(MetadataRegistry metadata, EntityMetadata entity, RelationshipId? via)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(entity);
        (_metadata, _entity, _via) = (metadata, entity, via);
    }

    public static SemanticQuery From(MetadataRegistry metadata, EntityId entity) =>
        new(metadata, metadata.Get(entity), null);

    public static SemanticQuery From(MetadataRegistry metadata, string entityName) =>
        new(metadata, metadata.Entities.FirstOrDefault(e => e.Name == entityName)
                      ?? throw new ArgumentException($"Unknown entity {entityName}"), null);

    /// <summary>Adds conditions; several calls and several arguments are combined with AND.</summary>
    public SemanticQuery Where(params SemanticFilterExpression[] conditions)
    {
        _filters.AddRange(conditions);
        return this;
    }

    public SemanticQuery WhereEq(FieldId field, object? value) =>
        Where(new SemanticFieldFilter(field, SemanticFilterOperator.Eq, value));

    public SemanticQuery WhereEq(string field, object? value) => WhereEq(FieldIdOf(field), value);

    public SemanticQuery WhereNeq(FieldId field, object? value) =>
        Where(new SemanticFieldFilter(field, SemanticFilterOperator.Neq, value));

    public SemanticQuery WhereNeq(string field, object? value) => WhereNeq(FieldIdOf(field), value);

    public SemanticQuery WhereIn(FieldId field, params object?[] values) =>
        Where(new SemanticFieldFilter(field, SemanticFilterOperator.In, values));

    public SemanticQuery WhereIn(string field, params object?[] values) => WhereIn(FieldIdOf(field), values);

    public SemanticQuery OrderBy(params SemanticOrderTerm[] terms)
    {
        _order.AddRange(terms);
        return this;
    }

    public SemanticQuery OrderBy(string field, SemanticSortDirection direction)
    {
        _order.Add(new SemanticOrderTerm(FieldIdOf(field), direction));
        return this;
    }

    public SemanticQuery Select(params FieldId[] fields)
    {
        _fields = fields;
        return this;
    }

    public SemanticQuery Select(params string[] names)
    {
        _fields = names.Select(FieldIdOf).ToArray();
        return this;
    }

    public SemanticQuery Select(IReadOnlyList<string> names) => Select(names.ToArray());

    public SemanticQuery Take(int limit)
    {
        _limit = limit;
        return this;
    }

    public SemanticQuery Skip(int offset)
    {
        _offset = offset;
        return this;
    }

    /// <summary>Loads a related collection; the lambda (optional) refines the child query.</summary>
    public SemanticQuery Include(RelationshipId relationship, Action<SemanticQuery>? configure = null)
    {
        var rel = _metadata.Get(relationship);
        var child = new SemanticQuery(_metadata, _metadata.Get(rel.Target), relationship);
        configure?.Invoke(child);
        _includes.Add(child);
        return this;
    }

    public SemanticOperation Build()
    {
        var next = 1;
        return new SemanticOperation(ToNode(ref next));
    }

    private SemanticReadNode ToNode(ref int next)
    {
        var id = next++;
        var children = new List<SemanticReadNode>();
        foreach (var include in _includes) children.Add(include.ToNode(ref next));

        SemanticFilterExpression? filter = _filters.Count switch
        {
            0 => null,
            1 => _filters[0],
            _ => new SemanticAndFilter(_filters.ToArray())
        };
        var options = filter is null && _order.Count == 0 && _limit is null && _offset is null
            ? null
            : new SemanticQueryOptions(filter, _order.ToArray(), _limit, _offset);
        var fields = _fields ?? _entity.EffectiveFields.Select(f => f.Id).ToArray();
        return new SemanticReadNode(id, _entity.EntityId, fields, _via, null, children, options);
    }

    private FieldId FieldIdOf(string name) =>
        _entity.EffectiveFields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Id ?? throw new ArgumentException($"Unknown field {_entity.Name}.{name}");
}
