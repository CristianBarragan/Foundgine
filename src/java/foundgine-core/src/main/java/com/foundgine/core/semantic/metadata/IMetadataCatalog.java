package com.foundgine.core.semantic.metadata;

public interface IMetadataCatalog extends IMetadataProvider {
	Iterable<EntityMetadata> entities();

	Iterable<RelationshipMetadata> relationships();
}
