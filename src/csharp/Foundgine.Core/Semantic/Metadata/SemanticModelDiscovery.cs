using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;

namespace Foundgine.Core.Semantic.Metadata;

/// <summary>
/// Bridges structural metadata to the semantic model. This lives in
/// Foundgine.Core.Semantic.Metadata (not Foundgine.Core.Semantic) so that the semantic model
/// itself carries no dependency on the concrete metadata assembly; only
/// applications that actually discover semantics from metadata need to
/// reference this type.
/// </summary>
public static class SemanticModelDiscovery
{
    /// <summary>
    /// Discovers the structural semantic model from Foundgine.Core.Semantic.Metadata.
    /// Metadata describes what exists; this method does not grant capability
    /// exposure or authorization. Applications can enrich the result with
    /// logical traversals and policy configuration afterwards.
    /// </summary>
    public static SemanticModel Discover(this IMetadataCatalog metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var entities = new Dictionary<EntityId, SemanticEntity>();
        foreach (var item in metadata.Entities)
        {
            var fields = item.EffectiveFields
                .Select(field => new SemanticField(
                    field.Id,
                    field.Name,
                    field.ClrType,
                    Aliases: field.Aliases?.Select(a => new SemanticAlias(a.Name, a.Weight)).ToArray()))
                .ToArray();

            var primary = item.PrimaryKey is null
                ? null
                : fields.FirstOrDefault(field =>
                    item.EffectiveFields.Any(source =>
                        source.Id == field.Id &&
                        source.Column?.ColumnId == item.PrimaryKey.ColumnId));

            if (primary is null)
                throw new InvalidOperationException(
                    $"Metadata entity '{item.Name}' has no field corresponding to its primary key.");

            entities[item.EntityId] = new SemanticEntity(
                item.EntityId,
                item.Name,
                new Foundgine.Core.Semantic.SemanticFieldIdentity(primary.Id, primary.Name),
                fields,
                [],
                item.Aliases?.Select(a => new SemanticAlias(a.Name, a.Weight)).ToArray())
            {
                ModelType = item.ClrType
            };
        }

        foreach (var relationship in metadata.Relationships)
        {
            if (!entities.TryGetValue(relationship.Source, out var source))
                throw new InvalidOperationException(
                    $"Relationship '{relationship.Name}' references unknown source entity '{relationship.Source}'.");

            if (!entities.ContainsKey(relationship.Target))
                throw new InvalidOperationException(
                    $"Relationship '{relationship.Name}' references unknown target entity '{relationship.Target}'.");

            var relationships = source.Relationships.ToList();
            relationships.Add(new SemanticRelationship(
                relationship.Id,
                relationship.Name,
                relationship.Target,
                relationship.IsCollection ? RelationshipCardinality.Many : RelationshipCardinality.One,
                relationship.Aliases?.Select(a => new SemanticAlias(a.Name, a.Weight)).ToArray()));

            entities[relationship.Source] = source with
            {
                Relationships = relationships.ToArray()
            };
        }

        return new SemanticModel(entities);
    }

    /// <summary>
    /// Starts a semantic configuration from structural metadata. Ordinary
    /// entities, fields, identities and direct relationships are discovered;
    /// subsequent builder calls are reserved for application meaning.
    /// </summary>
    public static SemanticModelBuilder FromMetadata(this IMetadataCatalog metadata) =>
        new SemanticModelBuilder().Import(metadata.Discover());
}