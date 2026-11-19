using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;

namespace Foundgine.Core.Mapping;

/// <summary>
/// Lowers a <see cref="MappingModel"/> into the existing metadata records so planners and providers
/// consume Domain -> Persistence mappings through the same <see cref="MetadataRegistry"/> as before.
/// IDs use the same derivations as the fluent compiler (EntityId from the domain name; ColumnId from
/// storage name + column), so a 1:1 mapping lowers to metadata identical to the compiler's.
/// A field may reference a column of any table the domain type uses; the owning table is recoverable
/// from <see cref="TableLayout"/>, never from the domain.
/// </summary>
public static class MappingBridge
{
    public static void Register(MappingModel model, MetadataRegistry registry)
    {
        foreach (var mapping in model.Mappings)
        {
            if (mapping.Tables.Count == 0) continue;
            registry.Register(Lower(mapping));
        }
        foreach (var mapping in model.Mappings)
            foreach (var rel in mapping.Relations)
                registry.Register(rel.JoinTable is null
                    ? LowerChildRelationship(model, mapping, rel)
                    : LowerManyToMany(model, mapping, rel));
    }

    /// <summary>
    /// Many-to-many: registered as a collection relationship between the two entities' keys (what a planner
    /// navigates); the join table that realises it is described by <see cref="ManyToMany"/>.
    /// </summary>
    private static RelationshipMetadata LowerManyToMany(MappingModel model, DomainMapping source, RelationBinding rel)
    {
        var target = model.Get(rel.TargetDomain);
        var sourcePk = source.Tables.First(t => t.Role == TableRole.Root);
        var targetPk = target.Tables.First(t => t.Role == TableRole.Root);
        var sourceId = EntityId.Create(source.Domain);
        var targetId = EntityId.Create(target.Domain);
        return new RelationshipMetadata(
            RelationshipId.Create(source.Domain, rel.Path), sourceId, targetId, rel.Path,
            new ColumnReference(sourceId, ColumnId.Create(sourcePk.Table, sourcePk.KeyColumn)),
            new ColumnReference(targetId, ColumnId.Create(targetPk.Table, targetPk.KeyColumn)),
            IsCollection: true);
    }

    public static IReadOnlyList<ManyToManyMetadata> ManyToMany(MappingModel model) =>
        model.Mappings.SelectMany(m => m.Relations.Where(r => r.JoinTable is not null).Select(r =>
            new ManyToManyMetadata(RelationshipId.Create(m.Domain, r.Path), m.Domain, r.TargetDomain, r.JoinTable!, r.LeftColumn!, r.RightColumn!))).ToList();

    public static EntityMetadata Lower(DomainMapping mapping)
    {
        var entityId = EntityId.Create(mapping.Domain);
        var primary = mapping.Tables.First(t => t.Role != TableRole.Join);
        var columns = new List<ColumnMetadata>();
        var fields = new List<FieldMetadata>();
        foreach (var table in mapping.Tables.Where(t => t.Role != TableRole.Join))
            foreach (var c in table.Columns)
            {
                var columnId = ColumnId.Create(table.Table, c.Column);
                columns.Add(new ColumnMetadata(columnId, c.Column));
                var type = c.ClrType is null ? typeof(object) : Type.GetType(c.ClrType) ?? typeof(object);
                fields.Add(new FieldMetadata(FieldId.Create(mapping.Domain, c.Path), c.Path, type, new ColumnReference(entityId, columnId)));
            }

        return new EntityMetadata(
            entityId, mapping.Domain, columns, primary.Table, fields,
            new ColumnReference(entityId, ColumnId.Create(primary.Table, primary.KeyColumn)));
    }

    private static RelationshipMetadata LowerChildRelationship(MappingModel model, DomainMapping source, RelationBinding rel)
    {
        var target = model.Get(rel.TargetDomain);
        var childTable = target.Tables.First(t => t.ParentTable is not null);
        var sourceId = EntityId.Create(source.Domain);
        var targetId = EntityId.Create(target.Domain);
        return new RelationshipMetadata(
            RelationshipId.Create(source.Domain, rel.Path), sourceId, targetId, rel.Path,
            new ColumnReference(sourceId, ColumnId.Create(childTable.ParentTable!, childTable.ParentKey!)),
            new ColumnReference(targetId, ColumnId.Create(childTable.Table, childTable.ForeignKey!)),
            IsCollection: true);
    }

    /// <summary>Which physical table (and column name) a ColumnId of this domain type lives in.</summary>
    public static IReadOnlyDictionary<ColumnId, (string Table, string Column)> TableLayout(DomainMapping mapping) =>
        mapping.Tables.Where(t => t.Role != TableRole.Join)
            .SelectMany(t => t.Columns.Select(c => (Id: ColumnId.Create(t.Table, c.Column), t.Table, c.Column)))
            .ToDictionary(x => x.Id, x => (x.Table, x.Column));
}

/// <summary>The join table behind a many-to-many relationship: JoinTable.LeftColumn -> source key, JoinTable.RightColumn -> target key.</summary>
public sealed record ManyToManyMetadata(RelationshipId Relationship, string SourceDomain, string TargetDomain, string JoinTable, string LeftColumn, string RightColumn);
