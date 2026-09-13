package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.*;
import java.util.*;
import java.util.function.Consumer;

/** Builds the static semantic topology used by resolution. */
public final class SemanticModelBuilder {
	private final Map<EntityId, SemanticEntity> entities = new LinkedHashMap<>();
	private final List<SemanticTraversal> traversals = new ArrayList<>();

	public SemanticModelBuilder entity(EntityId id, String name, Consumer<SemanticEntityBuilder> configure) {
		if (entities.containsKey(id))
			throw new IllegalStateException("Semantic entity '" + id + "' is already registered.");
		var b = new SemanticEntityBuilder(id, name);
		configure.accept(b);
		var e = b.build();
		validateEntityAliases(e);
		entities.put(id, e);
		return this;
	}

	public SemanticModelBuilder importModel(SemanticModel model) {
		for (var e : model.entities()) {
			if (entities.containsKey(e.id()))
				throw new IllegalStateException("Semantic entity '" + e.id() + "' is already registered.");
			entities.put(e.id(), e);
		}
		traversals.addAll(model.traversals());
		return this;
	}

	public SemanticModelBuilder overlay(SemanticModel overlay) {
		for (var oe : overlay.entities()) {
			var existing = entities.values().stream().filter(e -> e.name().equalsIgnoreCase(oe.name())).findFirst()
					.orElse(null);
			if (existing == null) {
				entities.put(oe.id(), oe);
				continue;
			}
			if (existing.modelType() != null && oe.modelType() != null && !existing.modelType().equals(oe.modelType()))
				throw new IllegalStateException("Incompatible CLR model types for '" + existing.name() + "'.");
			var fs = new ArrayList<>(existing.fields());
			for (var of : oe.fields()) {
				var idx = -1;
				for (int i = 0; i < fs.size(); i++)
					if (fs.get(i).name().equalsIgnoreCase(of.name())) {
						idx = i;
						break;
					}
				if (idx < 0) {
					fs.add(of);
					continue;
				}
				var f = fs.get(idx);
				if (!f.clrType().equals(of.clrType()))
					throw new IllegalStateException(
							"Incompatible CLR types for field '" + existing.name() + "." + f.name() + "'.");
				fs.set(idx,
						new SemanticField(f.id(), f.name(), f.clrType(),
								of.semanticType() != null ? of.semanticType() : f.semanticType(), of.capabilities(),
								merge(f.effectiveAliases(), of.effectiveAliases()),
								mergeConstraints(f.effectiveConstraints(), of.effectiveConstraints()),
								of.nullableOverride() != null ? of.nullableOverride() : f.nullableOverride()));
			}
			var rs = new ArrayList<>(existing.relationships());
			for (var or : oe.relationships()) {
				var idx = -1;
				for (int i = 0; i < rs.size(); i++)
					if (rs.get(i).name().equalsIgnoreCase(or.name())) {
						idx = i;
						break;
					}
				if (idx < 0) {
					rs.add(or);
					continue;
				}
				var r = rs.get(idx);
				if (!r.target().equals(or.target()) || r.cardinality() != or.cardinality())
					throw new IllegalStateException(
							"Conflicting semantic relationship '" + existing.name() + "." + r.name() + "'.");
				rs.set(idx, new SemanticRelationship(r.id(), r.name(), r.target(), r.cardinality(),
						merge(r.effectiveAliases(), or.effectiveAliases())));
			}
			entities.put(existing.id(), new SemanticEntity(existing.id(), existing.name(), existing.identity(), fs, rs,
					merge(existing.effectiveAliases(), oe.effectiveAliases()), existing.modelType()));
		}
		for (var t : overlay.traversals())
			if (traversals.stream()
					.noneMatch(x -> x.source().equals(t.source()) && x.name().equalsIgnoreCase(t.name())))
				traversals.add(t);
		return this;
	}

	public SemanticModelBuilder traversal(String sourceName, String name, String... relationshipNames) {
		var source = entities.values().stream().filter(e -> e.name().equalsIgnoreCase(sourceName)).findFirst()
				.orElseThrow(
						() -> new IllegalStateException("Source semantic entity '" + sourceName + "' is not known."));
		var path = new ArrayList<RelationshipId>();
		var current = source;
		for (var rn : relationshipNames) {
			var owner = current;
			var r = owner.relationships().stream().filter(x -> x.name().equalsIgnoreCase(rn)).findFirst()
					.orElseThrow(() -> new IllegalStateException("Traversal '" + name + "' references relationship '"
							+ rn + "', which is not declared on '" + owner.name() + "'."));
			path.add(r.id());
			current = entities.get(r.target());
		}
		return traversal(source.id(), name, path.toArray(RelationshipId[]::new));
	}

	public SemanticModelBuilder traversal(EntityId source, String name, RelationshipId... path) {
		if (path.length == 0)
			throw new IllegalArgumentException("A semantic traversal must contain at least one relationship.");
		if (!entities.containsKey(source))
			throw new IllegalStateException("Source semantic entity '" + source
					+ "' must be registered before declaring traversal '" + name + "'.");
		if (entities.get(source).relationships().stream().anyMatch(r -> r.name().equalsIgnoreCase(name))
				|| traversals.stream().anyMatch(t -> t.source().equals(source) && t.name().equalsIgnoreCase(name)))
			throw new IllegalStateException(
					"Semantic traversal '" + name + "' conflicts with an existing relationship or traversal on entity '"
							+ entities.get(source).name() + "'.");
		var current = source;
		for (var rid : path) {
			var e = entities.get(current);
			var r = e.relationships().stream().filter(x -> x.id().equals(rid)).findFirst()
					.orElseThrow(() -> new IllegalStateException("Traversal '" + name + "' references relationship '"
							+ rid + "', which is not declared on '" + e.name() + "'."));
			current = r.target();
		}
		traversals.add(new SemanticTraversal(source, name, current, List.of(path)));
		return this;
	}

	public SemanticModel build() {
		validateGlobalRelationshipIdentities();
		for (var e : entities.values()) {
			validateUnique(e);
			validateConstraints(e);
			for (var r : e.relationships())
				if (!entities.containsKey(r.target()))
					throw new IllegalStateException("Semantic relationship '" + e.name() + "." + r.name()
							+ "' targets unknown entity '" + r.target() + "'.");
		}
		return new SemanticModel(entities, traversals);
	}

	private void validateUnique(SemanticEntity e) {
		if (e.fields().stream().map(SemanticField::id).distinct().count() != e.fields().size())
			throw new IllegalStateException("Semantic entity '" + e.name() + "' contains duplicate field identities.");
		if (e.fields().stream().map(x -> x.name().toLowerCase(Locale.ROOT)).distinct().count() != e.fields().size())
			throw new IllegalStateException("Semantic entity '" + e.name() + "' contains duplicate field names.");
		if (e.relationships().stream().map(SemanticRelationship::id).distinct().count() != e.relationships().size())
			throw new IllegalStateException(
					"Semantic entity '" + e.name() + "' contains duplicate relationship identities.");
		if (e.relationships().stream().map(x -> x.name().toLowerCase(Locale.ROOT)).distinct().count() != e
				.relationships().size())
			throw new IllegalStateException(
					"Semantic entity '" + e.name() + "' contains duplicate relationship names.");
	}

	private void validateConstraints(SemanticEntity e) {
		for (var f : e.fields())
			for (var c : f.effectiveConstraints()) {
				if (c.kind() == SemanticConstraintKind.RANGE && c.minimum() == null && c.maximum() == null)
					throw new IllegalStateException("Field '" + e.name() + "." + f.name()
							+ "' declares a Range constraint without a minimum or maximum.");
				if (c.kind() != SemanticConstraintKind.RANGE && (c.value() == null || c.value().isBlank()))
					throw new IllegalStateException(
							"Field '" + e.name() + "." + f.name() + "' declares " + c.kind() + " without a value.");
			}
	}

	private void validateGlobalRelationshipIdentities() {
		var seen = new HashMap<RelationshipId, SemanticRelationship>();
		for (var e : entities.values())
			for (var r : e.relationships()) {
				var prior = seen.putIfAbsent(r.id(), r);
				if (prior != null && (!prior.name().equals(r.name()) || !prior.target().equals(r.target())
						|| prior.cardinality() != r.cardinality()))
					throw new IllegalStateException("Relationship identity conflict: '" + r.id() + "'.");
			}
	}

	private void validateEntityAliases(SemanticEntity e) {
		var names = new TreeSet<String>(String.CASE_INSENSITIVE_ORDER);
		names.add(e.name());
		for (var a : e.effectiveAliases()) {
			if (!names.add(a.name()))
				throw new IllegalStateException("Semantic entity alias '" + a.name()
						+ "' duplicates the canonical entity name or another alias.");
			if (entities.values().stream().anyMatch(x -> x.name().equalsIgnoreCase(a.name())
					|| x.effectiveAliases().stream().anyMatch(y -> y.name().equalsIgnoreCase(a.name()))))
				throw new IllegalStateException("Semantic entity alias '" + a.name()
						+ "' conflicts with an existing semantic entity name or alias.");
		}
	}

	private static List<SemanticAlias> merge(List<SemanticAlias> a, List<SemanticAlias> b) {
		var x = new ArrayList<>(a);
		for (var v : b)
			if (x.stream().noneMatch(y -> y.name().equalsIgnoreCase(v.name())))
				x.add(v);
		return List.copyOf(x);
	}

	private static List<SemanticConstraint> mergeConstraints(List<SemanticConstraint> a, List<SemanticConstraint> b) {
		var x = new ArrayList<>(a);
		for (var v : b)
			if (!x.contains(v))
				x.add(v);
		return List.copyOf(x);
	}
}
