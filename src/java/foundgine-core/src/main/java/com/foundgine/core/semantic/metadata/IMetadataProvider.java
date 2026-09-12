package com.foundgine.core.semantic.metadata;

import com.foundgine.core.abstractions.*;

public interface IMetadataProvider {
	EntityMetadata getEntity(EntityId entityId);

	RelationshipMetadata getRelationship(RelationshipId relationshipId);

	ModelMetadata getModel(ModelId modelId);

	ConnectionMetadata getConnection(ConnectionId connectionId);

	ConversionMetadata findConversion(Class<?> sourceType, Class<?> targetType);

	AuthorizationMetadata getAuthorization(AuthorizationId authorizationId);
}
