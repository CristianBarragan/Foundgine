using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Metadata;

/// <summary>
/// Runtime access to generated/static metadata. The core depends on this
/// abstraction rather than on a particular generator implementation.
/// </summary>
public interface IMetadataProvider
{
    EntityMetadata GetEntity(EntityId entityId);
    RelationshipMetadata GetRelationship(RelationshipId relationshipId);
    ModelMetadata GetModel(ModelId modelId);
    ConnectionMetadata GetConnection(ConnectionId connectionId);
    ConversionMetadata? FindConversion(Type sourceType, Type targetType);
    AuthorizationMetadata GetAuthorization(AuthorizationId authorizationId);
}
