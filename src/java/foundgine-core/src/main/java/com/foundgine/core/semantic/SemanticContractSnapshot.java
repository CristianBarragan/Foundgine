package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.*;
import java.util.*;

/** Immutable runtime representation of a frozen semantic contract. */
public final class SemanticContractSnapshot {
	private final Map<EntityId, SemanticEntity> entities;
	private final List<SemanticTraversal> traversals;
	private final Map<EntityId, Map<String, Integer>> entityAliasWeights;
	private final Map<EntityId, Map<FieldId, Map<String, Integer>>> fieldAliasWeights;
	private final Map<RelationshipId, Map<String, Integer>> relationshipAliasWeights;
	private final String contractFingerprint;

	public SemanticContractSnapshot(SemanticModel model) {
		model.ensureFrozen();
		entities = Collections.unmodifiableMap(new LinkedHashMap<>(
				model.entities().stream().collect(java.util.stream.Collectors.toMap(SemanticEntity::id,
						java.util.function.Function.identity(), (a, b) -> a, LinkedHashMap::new))));
		traversals = List.copyOf(model.traversals());
		entityAliasWeights = buildEntity();
		fieldAliasWeights = buildField();
		relationshipAliasWeights = buildRelationship();
		contractFingerprint = model.contractFingerprint();
	}

	private Map<EntityId, Map<String, Integer>> buildEntity() {
		var m = new LinkedHashMap<EntityId, Map<String, Integer>>();
		for (var e : entities.values())
			m.put(e.id(), aliasMap(e.effectiveAliases()));
		return Collections.unmodifiableMap(m);
	}

	private Map<EntityId, Map<FieldId, Map<String, Integer>>> buildField() {
		var m = new LinkedHashMap<EntityId, Map<FieldId, Map<String, Integer>>>();
		for (var e : entities.values()) {
			var x = new LinkedHashMap<FieldId, Map<String, Integer>>();
			for (var f : e.fields())
				x.put(f.id(), aliasMap(f.effectiveAliases()));
			m.put(e.id(), Collections.unmodifiableMap(x));
		}
		return Collections.unmodifiableMap(m);
	}

	private Map<RelationshipId, Map<String, Integer>> buildRelationship() {
		var m = new LinkedHashMap<RelationshipId, Map<String, Integer>>();
		for (var e : entities.values())
			for (var r : e.relationships())
				m.put(r.id(), aliasMap(r.effectiveAliases()));
		return Collections.unmodifiableMap(m);
	}

	private static Map<String, Integer> aliasMap(List<SemanticAlias> aliases) {
		var m = new TreeMap<String, Integer>(String.CASE_INSENSITIVE_ORDER);
		for (var a : aliases)
			if (a.weight() != null)
				m.merge(a.name(), a.weight(), Math::max);
		return Collections.unmodifiableMap(m);
	}

	public String contractFingerprint() {
		return contractFingerprint;
	}

	public Collection<SemanticEntity> entities() {
		return entities.values();
	}

	public List<SemanticTraversal> traversals() {
		return traversals;
	}

	public SemanticEntity get(EntityId id) {
		var e = entities.get(id);
		if (e == null)
			throw new IllegalStateException("Entity " + id + " has no semantic descriptor.");
		return e;
	}

	public SemanticEntity resolveEntity(String name) {
		return entities.values().stream()
				.filter(e -> e.name().equalsIgnoreCase(name)
						|| e.effectiveAliases().stream().anyMatch(a -> a.name().equalsIgnoreCase(name)))
				.findFirst()
				.orElseThrow(() -> new NoSuchElementException("Semantic entity '" + name + "' is not defined."));
	}

	public OptionalInt aliasWeight(EntityId id, String alias) {
		return weight(entityAliasWeights.get(id), alias);
	}

	public OptionalInt aliasWeight(EntityId e, FieldId f, String alias) {
		var x = fieldAliasWeights.get(e);
		return weight(x == null ? null : x.get(f), alias);
	}

	public OptionalInt aliasWeight(RelationshipId id, String alias) {
		return weight(relationshipAliasWeights.get(id), alias);
	}

	public boolean tryGetAlias(EntityId id, String alias, java.util.function.IntConsumer sink) {
		var w = aliasWeight(id, alias);
		if (w.isEmpty())
			return false;
		sink.accept(w.getAsInt());
		return true;
	}

	public boolean tryGetAlias(EntityId entityId, FieldId fieldId, String alias, java.util.function.IntConsumer sink) {
		var w = aliasWeight(entityId, fieldId, alias);
		if (w.isEmpty())
			return false;
		sink.accept(w.getAsInt());
		return true;
	}

	public boolean tryGetAlias(RelationshipId relationshipId, String alias, java.util.function.IntConsumer sink) {
		var w = aliasWeight(relationshipId, alias);
		if (w.isEmpty())
			return false;
		sink.accept(w.getAsInt());
		return true;
	}

	private static OptionalInt weight(Map<String, Integer> m, String a) {
		if (m == null)
			return OptionalInt.empty();
		var v = m.get(a);
		return v == null ? OptionalInt.empty() : OptionalInt.of(v);
	}
}
