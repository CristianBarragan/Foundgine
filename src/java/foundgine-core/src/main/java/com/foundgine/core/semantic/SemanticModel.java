package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.*;
import java.util.*;

/**
 * Immutable semantic topology produced by the semantic model builder/discovery
 * pipeline.
 */
public final class SemanticModel {
	private final Map<EntityId, SemanticEntity> entities;
	private final List<SemanticTraversal> traversals;
	private final String contractFingerprint;
	private final boolean frozen;

	public SemanticModel(Map<EntityId, SemanticEntity> entities, List<SemanticTraversal> traversals, boolean frozen) {
		this.entities = Collections.unmodifiableMap(new LinkedHashMap<>(entities));
		this.traversals = List.copyOf(traversals);
		this.frozen = frozen;
		this.contractFingerprint = SemanticModelFingerprint.compute(this);
	}

	public SemanticModel(Map<EntityId, SemanticEntity> entities, List<SemanticTraversal> traversals) {
		this(entities, traversals, false);
	}

	public Collection<SemanticEntity> entities() {
		return entities.values();
	}

	public List<SemanticTraversal> traversals() {
		return traversals;
	}

	public String contractFingerprint() {
		return contractFingerprint;
	}

	public boolean isFrozen() {
		return frozen;
	}

	public SemanticModel freeze() {
		return frozen ? this : new SemanticModel(entities, traversals, true);
	}

	public SemanticContractSnapshot createSnapshot() {
		ensureFrozen();
		return new SemanticContractSnapshot(this);
	}

	public void ensureFrozen() {
		if (!frozen)
			throw new IllegalStateException(
					"The semantic model must be frozen before it can be used as a trusted semantic contract.");
	}

	public boolean tryGet(EntityId id, java.util.function.Consumer<SemanticEntity> sink) {
		var e = entities.get(id);
		if (e == null)
			return false;
		sink.accept(e);
		return true;
	}

	public SemanticEntity get(EntityId id) {
		var e = entities.get(id);
		if (e == null)
			throw new NoSuchElementException("Entity " + id + " has no semantic descriptor.");
		return e;
	}

	public SemanticEntity resolveEntity(String name) {
		return entities.values().stream()
				.filter(e -> e.name().equalsIgnoreCase(name)
						|| e.effectiveAliases().stream().anyMatch(a -> a.name().equalsIgnoreCase(name)))
				.findFirst()
				.orElseThrow(() -> new NoSuchElementException("Semantic entity '" + name + "' is not defined."));
	}

	public Optional<SemanticEntity> findEntity(String name) {
		return entities.values().stream().filter(e -> e.name().equalsIgnoreCase(name)
				|| e.effectiveAliases().stream().anyMatch(a -> a.name().equalsIgnoreCase(name))).findFirst();
	}

	public SemanticTraversal getTraversal(EntityId source, String name) {
		return traversals.stream().filter(t -> t.source().equals(source) && t.name().equalsIgnoreCase(name)).findFirst()
				.orElseThrow(() -> new NoSuchElementException(
						"Semantic traversal '" + name + "' is not defined on entity '" + get(source).name() + "'."));
	}

	public Optional<SemanticTraversal> findTraversal(EntityId source, String name) {
		return traversals.stream().filter(t -> t.source().equals(source) && t.name().equalsIgnoreCase(name))
				.findFirst();
	}
}
