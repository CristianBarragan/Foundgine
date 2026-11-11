using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Providers.EntityFrameworkCore;

/// <summary>
/// Fluent builder for semantic reads. It only assembles the core IR (SemanticOperation); the planner and
/// authorizer still run unchanged inside <see cref="FoundgineEfReader"/>. Defaults: all fields of the entity;
/// an included relationship resolves its target entity and selects all of its fields from the metadata.
/// </summary>
public sealed class FoundgineQuery
{
    private readonly FoundgineEfReader _reader;
    private readonly MetadataRegistry _metadata;
    private readonly EntityId _entity;
    private readonly List<SemanticFilterExpression> _filters = [];
    private readonly List<SemanticOrderTerm> _order = [];
    private readonly List<FoundgineQuery> _includes = [];
    private readonly RelationshipId? _via;
    private IReadOnlyList<FieldId>? _fields;
    private int? _limit, _offset;

    internal FoundgineQuery(FoundgineEfReader reader, MetadataRegistry metadata, EntityId entity,
        RelationshipId? via = null)
    {
        (_reader, _metadata, _entity, _via) = (reader, metadata, entity, via);
    }

    /// <summary>Adds conditions; several calls and several arguments are combined with AND.</summary>
    public FoundgineQuery Where(params SemanticFilterExpression[] conditions)
    {
        _filters.AddRange(conditions);
        return this;
    }

    public FoundgineQuery OrderBy(params SemanticOrderTerm[] terms)
    {
        _order.AddRange(terms);
        return this;
    }

    public FoundgineQuery Select(params FieldId[] fields)
    {
        _fields = fields;
        return this;
    }

    public FoundgineQuery Take(int limit)
    {
        _limit = limit;
        return this;
    }

    public FoundgineQuery Skip(int offset)
    {
        _offset = offset;
        return this;
    }

    /// <summary>Loads a related collection; the lambda (optional) refines the child query.</summary>
    public FoundgineQuery Include(RelationshipId relationship, Action<FoundgineQuery>? configure = null)
    {
        var rel = _metadata.Relationships.FirstOrDefault(r => r.Id == relationship)
                  ?? throw new InvalidOperationException($"Relationship '{relationship}' is not in the model.");
        var child = new FoundgineQuery(_reader, _metadata, rel.Target, relationship);
        configure?.Invoke(child);
        _includes.Add(child);
        return this;
    }

    public SemanticOperation Build()
    {
        var next = 1;
        return new SemanticOperation(ToNode(ref next));
    }

    public Task<FoundgineReadResult> ReadAsync(CancellationToken ct = default) => _reader.ReadAsync(Build(), ct);

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
        var fields = _fields ?? _metadata.Get(_entity).EffectiveFields.Select(f => f.Id).ToArray();
        return new SemanticReadNode(id, _entity, fields, _via, null, children, options);
    }
}
