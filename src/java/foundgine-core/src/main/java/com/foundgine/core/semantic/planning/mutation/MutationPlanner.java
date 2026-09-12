package com.foundgine.core.semantic.planning.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.mutation.*;
import com.foundgine.core.semantic.query.*;
import java.util.*;

/** Converts mutation intent into provider-neutral mutation plans. */
public final class MutationPlanner {
	private final MutationSchema schema;

	public MutationPlanner(MutationSchema schema) {
		this.schema = Objects.requireNonNull(schema, "schema");
	}

	/**
	 * Lowers canonical semantic mutation IR into the existing provider-neutral
	 * mutation plan.
	 */
	public MutationBatchPlan plan(SemanticMutationOperationGraph graph) {
		Objects.requireNonNull(graph);
		if (graph.operations().isEmpty())
			throw new IllegalArgumentException("A semantic mutation graph must contain at least one operation.");
		var operations = new ArrayList<MutationOperation>();
		for (var op : graph.operations())
			operations.add(lower(op));
		return new MutationBatchPlan(operations, buildSemanticDependencies(graph, operations));
	}

	private MutationOperation lower(SemanticMutationOperation op) {
		var entity = schema.getEntity(op.entity());
		var fields = new ArrayList<MutationFieldValue>();
		for (var field : op.fields()) {
			var column = entity.fields().get(field.field());
			if (column == null)
				throw new IllegalStateException("Semantic mutation field '" + field.field().value()
						+ "' is not writable on '" + entity.name() + "'.");
			var source = field.source() == null ? null
					: new MutationValueReference(field.source().sourceOperationIndex(), field.source().sourceField());
			fields.add(new MutationFieldValue(column, field.value(), source));
		}
		List<ColumnId> conflicts = null;
		if (op.kind() == SemanticMutationKind.UPSERT) {
			if (op.conflictFields().isEmpty())
				throw new IllegalStateException(
						"Upsert for '" + entity.name() + "' requires semantic conflict fields.");
			conflicts = new ArrayList<>();
			for (var field : op.conflictFields()) {
				var column = entity.fields().get(field);
				if (column == null)
					throw new IllegalStateException("Semantic conflict field '" + field.value() + "' is not mapped on '"
							+ entity.name() + "'.");
				conflicts.add(column);
			}
		}
		validateSemanticOperation(op, entity);
		return new MutationOperation(entity, switch (op.kind()) {
		case CREATE -> MutationKind.CREATE;
		case UPDATE -> MutationKind.UPDATE;
		case DELETE -> MutationKind.DELETE;
		case UPSERT -> MutationKind.UPSERT;
		}, fields, op.filter(), conflicts, op.returnFields());
	}

	private void validateSemanticOperation(SemanticMutationOperation op, MutationEntitySchema entity) {
		if ((op.kind() == SemanticMutationKind.UPDATE || op.kind() == SemanticMutationKind.DELETE)
				&& op.filter() == null)
			throw new IllegalStateException(
					"Unfiltered " + op.kind() + " mutations are not permitted for '" + entity.name() + "'.");
		if (op.kind() == SemanticMutationKind.DELETE && !op.fields().isEmpty())
			throw new IllegalStateException("Delete mutations cannot contain field values.");
		if (op.kind() != SemanticMutationKind.DELETE && op.fields().isEmpty())
			throw new IllegalStateException(op.kind() + " mutations must contain at least one field value.");
		validateFilter(op.filter(), entity);
		for (var field : op.returnFields())
			if (!entity.fields().containsKey(field))
				throw new IllegalStateException(
						"Return field '" + field.value() + "' is not registered on '" + entity.name() + "'.");
	}

	private List<MutationDependency> buildSemanticDependencies(SemanticMutationOperationGraph graph,
			List<MutationOperation> operations) {
		var result = new ArrayList<MutationDependency>();
		for (int target = 0; target < operations.size(); target++) {
			var semantic = graph.operations().get(target);
			for (var field : semantic.fields())
				if (field.source() != null) {
					var source = field.source();
					validateSource(source.sourceOperationIndex(), target, operations.size());
					if (!operations.get(source.sourceOperationIndex()).returnFields().contains(source.sourceField()))
						throw new IllegalStateException("Semantic mutation operation " + target + " references field '"
								+ source.sourceField().value() + "' from operation " + source.sourceOperationIndex()
								+ ", but that field is not returned.");
					result.add(new MutationDependency(source.sourceOperationIndex(), target, source.sourceField(),
							operations.get(target).fields().stream()
									.filter(f -> f.source() != null
											&& f.source().sourceOperationIndex() == source.sourceOperationIndex()
											&& f.source().sourceField().equals(source.sourceField()))
									.findFirst().orElseThrow().column()));
				}
			for (var d : semantic.dependencies()) {
				validateSource(d.sourceOperationIndex(), target, operations.size());
				var targetOp = operations.get(target);
				var targetColumn = targetOp.entity().fields().get(d.targetField());
				if (targetColumn == null)
					throw new IllegalStateException("Semantic dependency target field '" + d.targetField().value()
							+ "' is not writable on '" + targetOp.entity().name() + "'.");
				final int targetIndex = target;
				boolean duplicate = result.stream()
						.anyMatch(x -> x.sourceOperationIndex() == d.sourceOperationIndex()
								&& x.targetOperationIndex() == targetIndex && x.sourceField().equals(d.sourceField())
								&& x.targetColumn().equals(targetColumn));
				if (!duplicate)
					result.add(new MutationDependency(d.sourceOperationIndex(), target, d.sourceField(), targetColumn));
			}
		}
		return List.copyOf(result);
	}

	private static void validateSource(int source, int target, int count) {
		if (source < 0 || source >= count || source >= target)
			throw new IllegalStateException("Mutation operation " + target
					+ " must reference an earlier operation; source " + source + " is invalid.");
	}

	public MutationPlan plan(MutationIntent intent) {
		Objects.requireNonNull(intent);
		var entity = schema.getEntity(intent.entity());
		validateDirect(intent.kind(), intent.fields(), intent.filter(), entity);
		for (var f : intent.fields())
			if (!entity.columns().contains(f.column()))
				throw new IllegalStateException(
						"Column '" + f.column().value() + "' is not registered on '" + entity.name() + "'.");
		List<FieldId> returns = intent.returnFields() != null ? intent.returnFields()
				: (intent.kind() == MutationKind.DELETE ? List.<FieldId>of()
						: new ArrayList<>(entity.fields().keySet()));
		validateReturnFields(returns, entity);
		return new MutationPlan(
				List.of(new MutationOperation(entity, intent.kind(), intent.fields(), intent.filter(), null, returns)));
	}

	public MutationPlan plan(UpsertIntent intent) {
		Objects.requireNonNull(intent);
		var entity = schema.getEntity(intent.entity());
		if (intent.fields().isEmpty())
			throw new IllegalStateException(
					"Upserts for '" + entity.name() + "' must contain at least one field value.");
		for (var f : intent.fields())
			if (!entity.columns().contains(f.column()))
				throw new IllegalStateException(
						"Column '" + f.column().value() + "' is not registered on '" + entity.name() + "'.");
		List<ColumnId> conflicts = intent.conflictColumns() != null ? intent.conflictColumns()
				: (entity.primaryKeyColumn() == null ? List.<ColumnId>of() : List.of(entity.primaryKeyColumn()));
		if (conflicts.isEmpty())
			throw new IllegalStateException(
					"Upsert for '" + entity.name() + "' requires conflict columns or a primary key.");
		for (var c : conflicts)
			if (!entity.columns().contains(c))
				throw new IllegalStateException(
						"Conflict column '" + c.value() + "' is not registered on '" + entity.name() + "'.");
		var returns = intent.returnFields() != null ? intent.returnFields() : new ArrayList<>(entity.fields().keySet());
		validateReturnFields(returns, entity);
		return new MutationPlan(
				List.of(new MutationOperation(entity, MutationKind.UPSERT, intent.fields(), null, conflicts, returns)));
	}

	public MutationBatchPlan plan(MutationBatchIntent intent) {
		Objects.requireNonNull(intent);
		if (intent.operations().isEmpty())
			throw new IllegalStateException("A mutation batch must contain at least one operation.");
		var ops = new ArrayList<MutationOperation>();
		for (var i : intent.operations())
			ops.add(planSingle(i));
		return new MutationBatchPlan(ops, buildDependencies(ops));
	}

	public MutationBatchPlan plan(List<NestedMutationIntent> batch) {
		Objects.requireNonNull(batch);
		if (batch.isEmpty())
			throw new IllegalStateException("A mutation batch must contain at least one item.");
		var ops = new ArrayList<MutationOperation>();
		var deps = new ArrayList<MutationDependency>();
		for (var item : batch) {
			var p = plan(item);
			int offset = ops.size();
			ops.addAll(p.operations());
			for (var d : p.dependencies())
				deps.add(new MutationDependency(d.sourceOperationIndex() + offset, d.targetOperationIndex() + offset,
						d.sourceField(), d.targetColumn()));
		}
		return new MutationBatchPlan(ops, deps);
	}

	public MutationBatchPlan plan(NestedMutationIntent intent) {
		Objects.requireNonNull(intent);
		var intents = new ArrayList<IMutationIntent>();
		var bindings = new ArrayList<Binding>();
		flatten(intent, null, null, intents, bindings);
		var ops = new ArrayList<MutationOperation>();
		for (var i : intents)
			ops.add(planSingle(i));
		for (var b : bindings) {
			var parent = ops.get(b.parent());
			var child = ops.get(b.child());
			var r = b.relationship();
			if (!r.source().equals(parent.entity().id()) || !r.target().equals(child.entity().id()))
				throw new IllegalStateException(
						"Nested mutation relationship '" + r.name() + "' does not match parent/child entities.");
			var pk = parent.entity().primaryKeyColumn();
			if (pk == null)
				throw new IllegalStateException("Parent entity '" + parent.entity().name()
						+ "' requires a primary key for nested mutation propagation.");
			if (!pk.equals(r.sourceColumn()))
				throw new IllegalStateException("Nested mutation relationship '" + r.name()
						+ "' requires the parent join column to be the parent primary key; found column '"
						+ r.sourceColumn().value() + "'.");
			if (child.fields().stream().anyMatch(f -> f.column().equals(r.targetColumn()) && f.source() == null))
				throw new IllegalStateException(
						"Child mutation '" + child.entity().name() + "' explicitly supplies relationship column '"
								+ r.targetColumn().value() + "'. Nested mutation propagation must own that value.");
			var pkField = parent.entity().fields().entrySet().stream().filter(e -> pk.equals(e.getValue()))
					.map(Map.Entry::getKey).findFirst()
					.orElseThrow(() -> new IllegalStateException("Primary key column '" + pk.value()
							+ "' has no field mapping on '" + parent.entity().name() + "'."));
			var returns = parent.returnFields() == null ? List.<FieldId>of() : parent.returnFields();
			if (!returns.contains(pkField))
				throw new IllegalStateException("Parent mutation '" + parent.entity().name()
						+ "' must return its primary key field '" + pkField.value() + "' for nested propagation.");
			var fields = new ArrayList<>(child.fields());
			fields.add(MutationFieldValue.fromPrevious(r.targetColumn(), b.parent(), pkField));
			ops.set(b.child(), new MutationOperation(child.entity(), child.kind(), fields, child.filter(),
					child.conflictColumns(), child.returnFields()));
		}
		return new MutationBatchPlan(ops, buildDependencies(ops));
	}

	private void flatten(NestedMutationIntent node, Integer parent, MutationRelationshipSchema relationship,
			List<IMutationIntent> intents, List<Binding> bindings) {
		int index = intents.size();
		intents.add(node.mutation());
		if (parent != null)
			bindings.add(new Binding(parent, index, relationship));
		for (var child : node.children()) {
			var r = schema.getRelationship(child.relationship());
			if (!r.source().equals(node.mutation().entity())
					|| !r.target().equals(child.mutation().mutation().entity()))
				throw new IllegalStateException(
						"Nested mutation relationship '" + r.name() + "' does not match nested mutation entities.");
			flatten(child.mutation(), index, r, intents, bindings);
		}
	}

	private MutationOperation planSingle(IMutationIntent intent) {
		if (intent instanceof MutationIntent m)
			return plan(m).operations().getFirst();
		if (intent instanceof UpsertIntent u)
			return plan(u).operations().getFirst();
		throw new UnsupportedOperationException("Unsupported mutation intent '" + intent.getClass().getName() + "'.");
	}

	private static List<MutationDependency> buildDependencies(List<MutationOperation> operations) {
		var d = new ArrayList<MutationDependency>();
		for (int target = 0; target < operations.size(); target++)
			for (var f : operations.get(target).fields())
				if (f.source() != null) {
					int source = f.source().sourceOperationIndex();
					if (source < 0 || source >= target)
						throw new IllegalStateException("Mutation operation " + target
								+ " must reference an earlier operation; source " + source + " is invalid.");
					if (!operations.get(source).returnFields().contains(f.source().sourceField()))
						throw new IllegalStateException("Mutation operation " + target + " references field '"
								+ f.source().sourceField().value() + "' from operation " + source
								+ ", but that field is not returned.");
					d.add(new MutationDependency(source, target, f.source().sourceField(), f.column()));
				}
		return d;
	}

	private static void validateDirect(MutationKind kind, List<MutationFieldValue> fields,
			SemanticFilterExpression filter, MutationEntitySchema entity) {
		if ((kind == MutationKind.UPDATE || kind == MutationKind.DELETE) && filter == null)
			throw new IllegalStateException(
					"Unfiltered " + kind + " mutations are not permitted for '" + entity.name() + "'.");
		if (kind == MutationKind.DELETE && !fields.isEmpty())
			throw new IllegalStateException("Delete mutations cannot contain field values.");
		if (kind != MutationKind.DELETE && fields.isEmpty())
			throw new IllegalStateException(kind + " mutations must contain at least one field value.");
		validateFilter(filter, entity);
	}

	private static void validateReturnFields(List<FieldId> fields, MutationEntitySchema entity) {
		for (var f : fields)
			if (!entity.fields().containsKey(f))
				throw new IllegalStateException(
						"Return field '" + f.value() + "' is not registered on '" + entity.name() + "'.");
	}

	private static void validateFilter(SemanticFilterExpression filter, MutationEntitySchema entity) {
		if (filter == null)
			return;
		if (filter instanceof SemanticFieldFilter f) {
			if (!entity.fields().containsKey(f.field()))
				throw new IllegalStateException(
						"Filter field '" + f.field().value() + "' is not registered on '" + entity.name() + "'.");
			return;
		}
		if (filter instanceof SemanticAggregateFilter)
			throw new UnsupportedOperationException(
					"Aggregate relationship filters are not valid mutation targets yet.");
		if (filter instanceof SemanticRelationshipFilter)
			throw new UnsupportedOperationException("Relationship filters are not valid mutation targets yet.");
		if (filter instanceof SemanticAndFilter a) {
			a.expressions().forEach(x -> validateFilter(x, entity));
			return;
		}
		if (filter instanceof SemanticOrFilter o) {
			o.expressions().forEach(x -> validateFilter(x, entity));
			return;
		}
		throw new UnsupportedOperationException(
				"Unsupported mutation filter '" + filter.getClass().getSimpleName() + "'.");
	}

	private record Binding(int parent, int child, MutationRelationshipSchema relationship) {
	}
}
