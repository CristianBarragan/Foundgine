package com.foundgine.providers.storage.sql.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.mutation.ExecutionMutationIR;
import com.foundgine.core.semantic.metadata.*;
import com.foundgine.core.semantic.planning.mutation.*;
import com.foundgine.providers.storage.sql.query.*;
import java.util.*;

public final class SqlMutationCompiler {
	private final IMetadataProvider metadata;

	public SqlMutationCompiler(IMetadataProvider metadata) {
		this.metadata = Objects.requireNonNull(metadata);
	}

	public SqlMutationBatchPlan compile(ExecutionMutationIR ir) {
		Objects.requireNonNull(ir);
		return compile(ir.toMutationBatchPlan());
	}

	public SqlMutationBatchPlan compile(MutationBatchPlan plan) {
		Objects.requireNonNull(plan);
		if (plan.operations().isEmpty())
			throw new IllegalArgumentException("A mutation batch must contain at least one operation.");
		List<SqlMutationPlan> ops = new ArrayList<>();
		for (var op : plan.operations())
			ops.add(compile(new MutationPlan(List.of(op))));
		return new SqlMutationBatchPlan(ops, plan.dependencies());
	}

	public SqlMutationPlan compile(MutationPlan plan) {
		Objects.requireNonNull(plan);
		if (plan.operations().size() != 1)
			throw new UnsupportedOperationException("A mutation plan must contain exactly one operation.");
		return switch (plan.operations().get(0).kind()) {
		case UPSERT -> compileUpsert(plan.operations().get(0));
		case CREATE -> compileCreate(plan.operations().get(0));
		case UPDATE -> compileUpdate(plan.operations().get(0));
		case DELETE -> compileDelete(plan.operations().get(0));
		};
	}

	private SqlMutationPlan compileUpsert(MutationOperation op) {
		var e = metadata.getEntity(op.entity().id());
		List<ColumnId> conflicts = op.conflictColumns() != null ? op.conflictColumns()
				: (e.primaryKey() != null ? List.of(e.primaryKey().columnId()) : List.of());
		if (conflicts.isEmpty())
			throw new IllegalArgumentException("Upsert '" + e.name() + "' has no conflict identity.");
		var fields = op.fields();
		StringBuilder sb = new StringBuilder("INSERT INTO ").append(table(e.effectiveStorageName())).append(" (");
		List<String> cols = new ArrayList<>();
		for (var f : fields)
			cols.add(q(resolveColumn(e, f.column())));
		sb.append(String.join(", ", cols)).append(") VALUES (");
		List<SqlParameterBinding> ps = new ArrayList<>();
		for (int i = 0; i < fields.size(); i++) {
			if (i > 0)
				sb.append(", ");
			sb.append("@p").append(i);
			ps.add(binding(e, fields.get(i), "p" + i));
		}
		sb.append(") ON CONFLICT (");
		List<String> cc = new ArrayList<>();
		for (var c : conflicts)
			cc.add(q(resolveColumn(e, c)));
		sb.append(String.join(", ", cc)).append(") ");
		var updates = fields.stream().filter(f -> !conflicts.contains(f.column())).toList();
		String fallback = null;
		if (updates.isEmpty())
			sb.append("DO NOTHING");
		else {
			sb.append("DO UPDATE SET ");
			for (int i = 0; i < updates.size(); i++) {
				if (i > 0)
					sb.append(", ");
				var f = updates.get(i);
				sb.append(q(resolveColumn(e, f.column()))).append(" = @p").append(fields.indexOf(f));
			}
			sb.append(" WHERE ");
			for (int i = 0; i < updates.size(); i++) {
				if (i > 0)
					sb.append(" OR ");
				String c = resolveColumn(e, updates.get(i).column());
				sb.append(table(e.effectiveStorageName())).append('.').append(q(c))
						.append(" IS DISTINCT FROM EXCLUDED.").append(q(c));
			}
			boolean canFallback = op.returnFields() != null && !op.returnFields().isEmpty()
					&& conflicts.stream().allMatch(c -> fields.stream().anyMatch(f -> f.column().equals(c)));
			if (canFallback) {
				StringBuilder fb = new StringBuilder("SELECT ");
				for (int i = 0; i < op.returnFields().size(); i++) {
					if (i > 0)
						fb.append(", ");
					var f = findField(e, op.returnFields().get(i));
					var c = resolveColumn(e, f.column().columnId());
					String rn = "r_" + f.id().value();
					fb.append(table(e.effectiveStorageName())).append('.').append(q(c)).append(" AS ").append(q(rn));
				}
				fb.append(" FROM ").append(table(e.effectiveStorageName())).append(" WHERE ");
				for (int i = 0; i < conflicts.size(); i++) {
					if (i > 0)
						fb.append(" AND ");
					var c = conflicts.get(i);
					int fi = firstFieldIndex(fields, c);
					fb.append(q(resolveColumn(e, c))).append(" IS NOT DISTINCT FROM @p").append(fi);
				}
				fb.append(" LIMIT 1");
				fallback = fb.toString();
			}
		}
		List<MutationReturnBinding> returns = appendReturning(sb, e, op.returnFields());
		return new SqlMutationPlan(sb.toString(), ps, returns, fallback);
	}

	private SqlMutationPlan compileCreate(MutationOperation op) {
		var e = metadata.getEntity(op.entity().id());
		var fs = op.fields();
		StringBuilder sb = new StringBuilder("INSERT INTO ").append(table(e.effectiveStorageName())).append(" (");
		List<String> cs = new ArrayList<>();
		for (var f : fs)
			cs.add(q(resolveColumn(e, f.column())));
		sb.append(String.join(", ", cs)).append(") VALUES (");
		List<SqlParameterBinding> ps = new ArrayList<>();
		for (int i = 0; i < fs.size(); i++) {
			if (i > 0)
				sb.append(", ");
			sb.append("@p").append(i);
			ps.add(binding(e, fs.get(i), "p" + i));
		}
		sb.append(')');
		return new SqlMutationPlan(sb.toString() + returningSql(e, op.returnFields()), ps,
				returnBindings(e, op.returnFields()));
	}

	private SqlMutationPlan compileUpdate(MutationOperation op) {
		if (op.filter() == null)
			throw new IllegalArgumentException("Update requires a filter.");
		var e = metadata.getEntity(op.entity().id());
		StringBuilder sb = new StringBuilder("UPDATE ").append(table(e.effectiveStorageName())).append(" SET ");
		List<SqlParameterBinding> ps = new ArrayList<>();
		for (int i = 0; i < op.fields().size(); i++) {
			if (i > 0)
				sb.append(", ");
			var f = op.fields().get(i);
			sb.append(q(resolveColumn(e, f.column()))).append(" = @p").append(i);
			ps.add(binding(e, f, "p" + i));
		}
		String where = SemanticQuerySqlWriter.writeWhere(op.filter(), e, "t0", ps, metadata,
				com.foundgine.core.semantic.planning.AggregateExecutionStrategy.DEFAULT);
		where = where.replace("\"t0\".", table(e.effectiveStorageName()) + ".");
		sb.append(" WHERE ").append(where);
		sb.append(returningSql(e, op.returnFields()));
		return new SqlMutationPlan(sb.toString(), ps, returnBindings(e, op.returnFields()));
	}

	private SqlMutationPlan compileDelete(MutationOperation op) {
		if (op.filter() == null)
			throw new IllegalArgumentException("Delete requires a filter.");
		var e = metadata.getEntity(op.entity().id());
		List<SqlParameterBinding> ps = new ArrayList<>();
		String where = SemanticQuerySqlWriter
				.writeWhere(op.filter(), e, "t0", ps, metadata,
						com.foundgine.core.semantic.planning.AggregateExecutionStrategy.DEFAULT)
				.replace("\"t0\".", table(e.effectiveStorageName()) + ".");
		return new SqlMutationPlan("DELETE FROM " + table(e.effectiveStorageName()) + " WHERE " + where, ps, List.of());
	}

	private SqlParameterBinding binding(EntityMetadata e, MutationFieldValue f, String n) {
		var field = e.effectiveFields().stream()
				.filter(x -> x.column() != null && x.column().columnId().equals(f.column())).findFirst().orElseThrow();
		return new SqlParameterBinding(n, f.value(), f.source(), null, field.clrType());
	}

	private List<MutationReturnBinding> appendReturning(StringBuilder sb, EntityMetadata e, List<FieldId> fs) {
		sb.append(returningSql(e, fs));
		return returnBindings(e, fs);
	}

	private String returningSql(EntityMetadata e, List<FieldId> fs) {
		var r = returnBindings(e, fs);
		if (r.isEmpty())
			return "";
		StringBuilder s = new StringBuilder(" RETURNING ");
		for (int i = 0; i < r.size(); i++) {
			if (i > 0)
				s.append(", ");
			var f = findField(e, r.get(i).fieldId());
			s.append(q(resolveColumn(e, f.column().columnId()))).append(" AS ").append(q(r.get(i).resultName()));
		}
		return s.toString();
	}

	private List<MutationReturnBinding> returnBindings(EntityMetadata e, List<FieldId> fs) {
		List<FieldId> requested = fs != null && !fs.isEmpty() ? fs
				: e.effectiveFields().stream().filter(f -> f.column() != null).map(FieldMetadata::id).toList();
		List<MutationReturnBinding> r = new ArrayList<>();
		for (var id : requested)
			r.add(new MutationReturnBinding(id, "r_" + id.value()));
		return r;
	}

	private FieldMetadata findField(EntityMetadata e, FieldId id) {
		return e.effectiveFields().stream().filter(f -> f.id().equals(id)).findFirst()
				.orElseThrow(() -> new IllegalArgumentException("Unknown return field '" + id + "'."));
	}

	private int firstFieldIndex(List<MutationFieldValue> fs, ColumnId c) {
		for (int i = 0; i < fs.size(); i++)
			if (fs.get(i).column().equals(c))
				return i;
		return -1;
	}

	private String resolveColumn(EntityMetadata e, ColumnId id) {
		return e.columns().stream().filter(c -> c.id().equals(id)).map(ColumnMetadata::effectiveStorageName).findFirst()
				.orElseThrow(() -> new IllegalArgumentException(
						"Column '" + id.value() + "' is not registered on '" + e.name() + "'."));
	}

	private String resolveColumn(EntityMetadata e, FieldMetadata f) {
		if (f.column() == null)
			throw new IllegalArgumentException("Field '" + f.name() + "' has no storage column mapping.");
		return resolveColumn(e, f.column().columnId());
	}

	private static String q(String s) {
		return "\"" + s.replace("\"", "\"\"") + "\"";
	}

	private static String table(String s) {
		return Arrays.stream(s.split("\\.")).filter(x -> !x.isBlank()).map(String::trim).map(SqlMutationCompiler::q)
				.reduce((a, b) -> a + "." + b).orElseThrow();
	}
}
