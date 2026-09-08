using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Generated;

namespace Foundgine.SupplyChain.Advanced.OpenIntent;

/// <summary>
/// Compatibility lookup for the closed sample handlers. It exposes identities
/// from generated metadata; it does not construct or own a semantic model.
/// </summary>
public static class OpenIntentSemanticIds
{
    private static readonly IMetadataCatalog Metadata = GeneratedMetadata.Build();

    public static EntityId Entity(string name) =>
        Metadata.Entities.Single(e =>
            string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)).EntityId;

    public static FieldId Field(string entity, string field) =>
        Metadata.Entities.Single(e =>
            string.Equals(e.Name, entity, StringComparison.OrdinalIgnoreCase))
            .EffectiveFields.Single(f =>
                string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase)).Id;

    public static RelationshipId Relationship(string entity, string relationship) =>
        Metadata.Relationships.Single(r =>
            string.Equals(r.Name, relationship, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                Metadata.Entities.Single(e => e.EntityId == r.Source).Name,
                entity,
                StringComparison.OrdinalIgnoreCase)).Id;
}