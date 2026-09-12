package com.foundgine.core.semantic.metadata;

import com.foundgine.core.abstractions.*;
import java.util.*;

/** In-memory registry of static metadata. */
public final class MetadataRegistry implements IMetadataCatalog {
	private final Map<EntityId, EntityMetadata> entities = new LinkedHashMap<>();
	private final Map<RelationshipId, RelationshipMetadata> relationships = new LinkedHashMap<>();
	private final Map<ModelId, ModelMetadata> models = new LinkedHashMap<>();
	private final Map<ConnectionId, ConnectionMetadata> connections = new LinkedHashMap<>();
	private final List<ConversionMetadata> conversions = new ArrayList<>();
	private final Map<AuthorizationId, AuthorizationMetadata> authorizations = new LinkedHashMap<>();

	@Override
	public Iterable<EntityMetadata> entities() {
		return Collections.unmodifiableCollection(entities.values());
	}

	@Override
	public Iterable<RelationshipMetadata> relationships() {
		return Collections.unmodifiableCollection(relationships.values());
	}

	public Iterable<ModelMetadata> models() {
		return Collections.unmodifiableCollection(models.values());
	}

	public Iterable<ConnectionMetadata> connections() {
		return Collections.unmodifiableCollection(connections.values());
	}

	public Iterable<ConversionMetadata> conversions() {
		return Collections.unmodifiableList(conversions);
	}

	public Iterable<AuthorizationMetadata> authorizations() {
		return Collections.unmodifiableCollection(authorizations.values());
	}

	public void register(EntityMetadata metadata) {
		require(metadata);
		entities.put(metadata.entityId(), metadata);
	}

	public void register(RelationshipMetadata metadata) {
		require(metadata);
		relationships.put(metadata.id(), metadata);
	}

	public void register(ModelMetadata metadata) {
		require(metadata);
		models.put(metadata.id(), metadata);
	}

	public void register(ConnectionMetadata metadata) {
		require(metadata);
		connections.put(metadata.id(), metadata);
	}

	public void register(AuthorizationMetadata metadata) {
		require(metadata);
		authorizations.put(metadata.id(), metadata);
	}

	public void register(ConversionMetadata conversion) {
		require(conversion);
		if (conversions.stream()
				.anyMatch(x -> x.sourceType() == conversion.sourceType() && x.targetType() == conversion.targetType()))
			throw new IllegalStateException("Duplicate Foundgine conversion " + conversion.sourceType() + " -> "
					+ conversion.targetType() + ".");
		conversions.add(conversion);
	}

	public boolean tryGet(EntityId id) {
		return entities.containsKey(id);
	}

	public EntityMetadata get(EntityId id) {
		return requireGet(entities, id, "Entity");
	}

	@Override
	public EntityMetadata getEntity(EntityId id) {
		return get(id);
	}

	public boolean tryGet(ModelId id) {
		return models.containsKey(id);
	}

	public ModelMetadata get(ModelId id) {
		return requireGet(models, id, "Model");
	}

	@Override
	public ModelMetadata getModel(ModelId id) {
		return get(id);
	}

	public boolean tryGet(ConnectionId id) {
		return connections.containsKey(id);
	}

	public ConnectionMetadata get(ConnectionId id) {
		return requireGet(connections, id, "Connection");
	}

	@Override
	public ConnectionMetadata getConnection(ConnectionId id) {
		return get(id);
	}

	public boolean tryGet(RelationshipId id) {
		return relationships.containsKey(id);
	}

	public RelationshipMetadata get(RelationshipId id) {
		return requireGet(relationships, id, "Relationship");
	}

	@Override
	public RelationshipMetadata getRelationship(RelationshipId id) {
		return get(id);
	}

	@Override
	public AuthorizationMetadata getAuthorization(AuthorizationId id) {
		return requireGet(authorizations, id, "Authorization");
	}

	@Override
	public ConversionMetadata findConversion(Class<?> sourceType, Class<?> targetType) {
		return conversions.stream().filter(x -> x.sourceType() == sourceType && x.targetType() == targetType)
				.findFirst().orElse(null);
	}

	private static void require(Object value) {
		Objects.requireNonNull(value);
	}

	private static <K, V> V requireGet(Map<K, V> map, K key, String kind) {
		V value = map.get(key);
		if (value == null)
			throw new NoSuchElementException(kind + " " + key + " was not registered.");
		return value;
	}
}
