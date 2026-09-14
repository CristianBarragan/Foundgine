package com.foundgine.core.semantic.mutation;

import com.foundgine.core.abstractions.FieldId;
import java.util.*;

/** Validates and organizes semantic mutation value-flow dependencies. */
public final class SemanticMutationPlanner {
	public SemanticMutationPlan plan(SemanticMutationOperationGraph graph) {
		Objects.requireNonNull(graph);
		if (graph.operations().isEmpty())
			throw new IllegalArgumentException("A semantic mutation graph must contain at least one operation.");
		var ops = new ArrayList<SemanticMutationPlan.SemanticMutationOperationPlan>();
		for (int i = 0; i < graph.operations().size(); i++) {
			var op = graph.operations().get(i);
			ops.add(new SemanticMutationPlan.SemanticMutationOperationPlan(Integer.toString(i), op.entity(), op.kind(),
					op.fields(), op.filter(), op.conflictFields(), op.returnFields(), op.effects()));
		}
		var dependencies = new LinkedHashMap<DependencyKey, SemanticMutationPlan.SemanticMutationDependencyPlan>();
		for (int target = 0; target < graph.operations().size(); target++) {
			var op = graph.operations().get(target);
			for (var field : op.fields())
				if (field.source() != null) {
					var source = field.source();
					validateSourceIndex(source.sourceOperationIndex(), target, graph.operations().size());
					validateReturned(graph.operations().get(source.sourceOperationIndex()), source.sourceField(),
							source.sourceOperationIndex(), target);
					var d = new SemanticMutationPlan.SemanticMutationDependencyPlan(id(source.sourceOperationIndex()),
							id(target), source.sourceField(), field.field());
					dependencies.put(
							new DependencyKey(d.fromOperationId(), d.toOperationId(), d.sourceField(), d.targetField()),
							d);
				}
			for (var d : op.dependencies()) {
				validateSourceIndex(d.sourceOperationIndex(), target, graph.operations().size());
				validateReturned(graph.operations().get(d.sourceOperationIndex()), d.sourceField(),
						d.sourceOperationIndex(), target);
				var planned = new SemanticMutationPlan.SemanticMutationDependencyPlan(id(d.sourceOperationIndex()),
						id(target), d.sourceField(), d.targetField(), d.relationship());
				var key = new DependencyKey(planned.fromOperationId(), planned.toOperationId(), planned.sourceField(),
						planned.targetField());
				var existing = dependencies.get(key);
				if (existing != null && existing.relationship() == null && planned.relationship() != null)
					dependencies.put(key, planned);
				else
					dependencies.putIfAbsent(key, planned);
			}
		}
		var result = new ArrayList<>(dependencies.values());
		result.sort(Comparator
				.comparingInt((SemanticMutationPlan.SemanticMutationDependencyPlan d) -> Integer
						.parseInt(d.fromOperationId()))
				.thenComparingInt(d -> Integer.parseInt(d.toOperationId()))
				.thenComparingLong(d -> d.sourceField().value()).thenComparingLong(d -> d.targetField().value()));
		return new SemanticMutationPlan(ops, result);
	}

	private static String id(int i) {
		return Integer.toString(i);
	}

	private static void validateSourceIndex(int source, int target, int count) {
		if (source < 0 || source >= count || source >= target)
			throw new IllegalStateException("Semantic mutation dependency " + source + " -> " + target
					+ " is invalid; dependencies must point to an earlier operation.");
	}

	private static void validateReturned(SemanticMutationOperation source, FieldId field, int sourceIndex,
			int targetIndex) {
		if (!source.returnFields().contains(field))
			throw new IllegalStateException(
					"Semantic mutation dependency " + sourceIndex + " -> " + targetIndex + " references field '"
							+ field.value() + "', but the source operation does not return that field.");
	}

	private record DependencyKey(String fromOperationId, String toOperationId, FieldId sourceField,
			FieldId targetField) {
	}
}
