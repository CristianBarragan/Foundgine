using System.Diagnostics.CodeAnalysis;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;

namespace Foundgine.Providers.Orm;

/// <summary>A relationship resolved, for one traversal direction, to concrete key fields on both entities.</summary>
public sealed record OrmLink(RelationshipId Relationship, EntityId ParentEntity, EntityId ChildEntity,
    FieldId ParentField, FieldId ChildField);

/// <summary>
/// AOT expression registry: generated entity maps bound to semantic ids, plus resolved relationship links.
/// Built once at startup; a mismatch between generated maps and metadata fails here, not at query time.
/// </summary>
public sealed class OrmRegistry
{
    private readonly Dictionary<EntityId, IOrmEntityMap> _maps;
    private readonly Dictionary<(RelationshipId, EntityId), OrmLink> _links;
    private readonly Dictionary<(RelationshipId, EntityId), string> _unresolved;

    private OrmRegistry(
        Dictionary<EntityId, IOrmEntityMap> maps,
        Dictionary<(RelationshipId, EntityId), OrmLink> links,
        Dictionary<(RelationshipId, EntityId), string> unresolved)
    {
        _maps = maps;
        _links = links;
        _unresolved = unresolved;
    }

    /// <param name="entities">Entity metadata (for generated metadata: <c>IMetadataSource.Entities</c>).</param>
    /// <param name="maps">Generated maps, e.g. <c>GeneratedOrmMaps.CreateAll()</c>.</param>
    /// <param name="relationships">Relationship metadata (<c>IMetadataSource.Relationships</c>); required for
    /// traversal. Relationships whose key columns are not exposed as mapped fields are recorded as unresolved
    /// and fail closed, with a precise message, only if a plan actually traverses them.</param>
    public static OrmRegistry Create(
        IEnumerable<EntityMetadata> entities,
        IEnumerable<IOrmEntityMap> maps,
        IEnumerable<RelationshipMetadata>? relationships = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(maps);

        var byName = entities.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var result = new Dictionary<EntityId, IOrmEntityMap>();
        foreach (var map in maps)
        {
            if (!byName.TryGetValue(map.EntityName, out var entity))
                throw new InvalidOperationException(
                    $"Generated ORM map '{map.EntityName}' has no matching entity in the supplied metadata.");
            map.Bind(entity);
            result.Add(entity.EntityId, map);
        }

        var links = new Dictionary<(RelationshipId, EntityId), OrmLink>();
        var unresolved = new Dictionary<(RelationshipId, EntityId), string>();
        foreach (var relationship in relationships ?? [])
        {
            if (result.TryGetValue(relationship.Source, out var sourceMap) &&
                result.TryGetValue(relationship.Target, out var targetMap))
                sourceMap.BindRelationship(relationship, targetMap);
            Resolve(relationship, relationship.Source, relationship.Target,
                relationship.SourceKey, relationship.TargetKey, result, links, unresolved);
            if (relationship.Source != relationship.Target)
                Resolve(relationship, relationship.Target, relationship.Source,
                    relationship.TargetKey, relationship.SourceKey, result, links, unresolved);
        }

        return new OrmRegistry(result, links, unresolved);
    }

    private static void Resolve(
        RelationshipMetadata relationship,
        EntityId parent,
        EntityId child,
        ColumnReference parentKey,
        ColumnReference childKey,
        Dictionary<EntityId, IOrmEntityMap> maps,
        Dictionary<(RelationshipId, EntityId), OrmLink> links,
        Dictionary<(RelationshipId, EntityId), string> unresolved)
    {
        var slot = (relationship.Id, parent);
        if (!maps.TryGetValue(parent, out var parentMap) || !maps.TryGetValue(child, out var childMap))
        {
            unresolved[slot] = $"relationship '{relationship.Name}' touches an entity with no generated ORM map";
            return;
        }

        var parentField = parentMap.FieldForColumn(parentKey.ColumnId);
        var childField = childMap.FieldForColumn(childKey.ColumnId);
        if (parentField is null || childField is null)
        {
            unresolved[slot] =
                $"relationship '{relationship.Name}' key column is not exposed as a field of " +
                $"'{(parentField is null ? parentMap.EntityName : childMap.EntityName)}'";
            return;
        }

        links[slot] = new OrmLink(relationship.Id, parent, child, parentField.Value, childField.Value);
    }

    public bool TryGetMap(EntityId id, [MaybeNullWhen(false)] out IOrmEntityMap map) => _maps.TryGetValue(id, out map);

    public IOrmEntityMap GetMap(EntityId id) =>
        _maps.TryGetValue(id, out var map)
            ? map
            : throw new NotSupportedException(
                $"Entity '{id.Value}' has no generated ORM map; it is outside this deployment's capability set.");

    /// <summary>The link for traversing <paramref name="relationship"/> from <paramref name="parentEntity"/>.</summary>
    public OrmLink GetLink(RelationshipId relationship, EntityId parentEntity)
    {
        if (_links.TryGetValue((relationship, parentEntity), out var link)) return link;
        throw new NotSupportedException(
            _unresolved.TryGetValue((relationship, parentEntity), out var reason)
                ? $"ORM provider: cannot traverse {reason}."
                : $"ORM provider: relationship '{relationship.Value}' is unknown from entity '{parentEntity.Value}'; " +
                  "pass IMetadataSource.Relationships to OrmRegistry.Create.");
    }
}
