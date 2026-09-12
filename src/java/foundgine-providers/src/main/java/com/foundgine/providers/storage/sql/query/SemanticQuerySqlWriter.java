package com.foundgine.providers.storage.sql.query;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.metadata.*;
import com.foundgine.core.semantic.planning.AggregateExecutionStrategy;
import com.foundgine.core.semantic.planning.AggregateExecutionStrategyResolver;
import com.foundgine.core.semantic.query.*;
import com.foundgine.providers.storage.sql.SqlCompiler;
import java.lang.reflect.Array;
import java.util.*;

/**
 * Lowers semantic query filters and ordering into parameterized SQL fragments.
 */
public final class SemanticQuerySqlWriter {
	private SemanticQuerySqlWriter() {
	}

	public static String writeWhere(SemanticFilterExpression filter, EntityMetadata entity, String alias,
			Collection<SqlParameterBinding> parameters, IMetadataProvider metadata,
			AggregateExecutionStrategy aggregateStrategy) {
		if (filter == null)
			return null;
		int[] counters = { parameters.size(), 0 };
		return writeFilter(filter, entity, alias, parameters, metadata, aggregateStrategy, true, counters);
	}

	public static String writeOrder(List<SemanticOrderTerm> terms, EntityMetadata entity, String alias) {
		if (terms == null || terms.isEmpty())
			return null;
		List<String> parts = new ArrayList<>();
		for (SemanticOrderTerm term : terms) {
			FieldMetadata field = entity.effectiveFields().stream().filter(x -> x.id().equals(term.field())).findFirst()
					.orElseThrow(() -> new IllegalArgumentException(
							"Unknown order field '" + term.field() + "' on '" + entity.name() + "'."));
			ColumnMetadata column = resolveColumn(entity, field);
			parts.add(SqlCompiler.quoteIdentifier(alias) + "."
					+ SqlCompiler.quoteIdentifier(column.effectiveStorageName())
					+ (term.direction() == SemanticSortDirection.DESC ? " DESC" : " ASC"));
		}
		return "ORDER BY " + String.join(", ", parts);
	}

	private static String writeFilter(SemanticFilterExpression expression, EntityMetadata entity, String alias,
			Collection<SqlParameterBinding> parameters, IMetadataProvider metadata,
			AggregateExecutionStrategy aggregateStrategy, boolean allowAggregateStrategy, int[] counters) {
		if (expression instanceof SemanticFieldFilter field) {
			FieldMetadata fm = entity.effectiveFields().stream().filter(x -> x.id().equals(field.field())).findFirst()
					.orElseThrow(() -> new IllegalArgumentException(
							"Unknown filter field '" + field.field() + "' on '" + entity.name() + "'."));
			ColumnMetadata column = resolveColumn(entity, fm);
			String ref = SqlCompiler.quoteIdentifier(alias) + "."
					+ SqlCompiler.quoteIdentifier(column.effectiveStorageName());
			if (field.operator() == SemanticFilterOperator.EQ && field.value() == null)
				return ref + " IS NULL";
			if (field.operator() == SemanticFilterOperator.NEQ && field.value() == null)
				return ref + " IS NOT NULL";
			if (field.operator() == SemanticFilterOperator.IN) {
				List<Object> values = normalizeList(field.value());
				if (values.isEmpty())
					return "1 = 0";
				List<String> refs = new ArrayList<>();
				for (Object value : values) {
					String name = "p" + counters[0]++;
					parameters.add(new SqlParameterBinding(name, value, null, null, fm.clrType()));
					refs.add("@" + name);
				}
				return ref + " IN (" + String.join(", ", refs) + ")";
			}
			String name = "p" + counters[0]++;
			parameters.add(new SqlParameterBinding(name, field.value(), null, null, fm.clrType()));
			return ref + comparisonOperatorSql(field.operator()) + "@" + name;
		}
		if (expression instanceof SemanticRelationshipFilter rf)
			return writeRelationshipFilter(rf, entity, alias, parameters, metadata, aggregateStrategy, counters);
		if (expression instanceof SemanticAggregateFilter af)
			return writeAggregateFilter(af, entity, alias, parameters, metadata, aggregateStrategy,
					allowAggregateStrategy, counters);
		if (expression instanceof SemanticAndFilter and)
			return join(and.expressions(), "AND", entity, alias, parameters, metadata, aggregateStrategy,
					allowAggregateStrategy, counters);
		if (expression instanceof SemanticOrFilter or)
			return join(or.expressions(), "OR", entity, alias, parameters, metadata, aggregateStrategy,
					allowAggregateStrategy, counters);
		throw new UnsupportedOperationException(expression.getClass().getSimpleName());
	}

	private static String writeRelationshipFilter(SemanticRelationshipFilter filter, EntityMetadata source,
			String sourceAlias, Collection<SqlParameterBinding> parameters, IMetadataProvider metadata,
			AggregateExecutionStrategy strategy, int[] counters) {
		RelationshipMetadata relationship = metadata.getRelationship(filter.relationship());
		if (!relationship.source().equals(source.entityId()))
			throw new IllegalArgumentException(
					"Relationship '" + relationship.name() + "' is not a relationship from '" + source.name() + "'.");
		EntityMetadata target = metadata.getEntity(relationship.target());
		String targetAlias = "s" + counters[1]++;
		String join = renderJoinCondition(relationship.sourceKey(), relationship.targetKey(), source, sourceAlias,
				target, targetAlias);
		String predicate = writeFilter(filter.predicate(), target, targetAlias, parameters, metadata, strategy, false,
				counters);
		String exists = "EXISTS (SELECT 1 FROM " + SqlCompiler.quoteStorageName(target.effectiveStorageName()) + " "
				+ SqlCompiler.quoteIdentifier(targetAlias) + " WHERE " + join + " AND " + predicate + ")";
		return switch (filter.quantifier()) {
		case SOME -> exists;
		case NONE -> "NOT " + exists;
		case ALL -> "NOT EXISTS (SELECT 1 FROM " + SqlCompiler.quoteStorageName(target.effectiveStorageName()) + " "
				+ SqlCompiler.quoteIdentifier(targetAlias) + " WHERE " + join + " AND NOT (" + predicate + "))";
		};
	}

	private static String writeAggregateFilter(SemanticAggregateFilter filter, EntityMetadata source,
			String sourceAlias, Collection<SqlParameterBinding> parameters, IMetadataProvider metadata,
			AggregateExecutionStrategy strategy, boolean allowStrategy, int[] counters) {
		RelationshipMetadata relationship = metadata.getRelationship(filter.relationship());
		if (!relationship.source().equals(source.entityId()))
			throw new IllegalArgumentException(
					"Relationship '" + relationship.name() + "' is not a relationship from '" + source.name() + "'.");
		EntityMetadata target = metadata.getEntity(relationship.target());
		String targetAlias = "a" + counters[1]++;
		String join = renderJoinCondition(relationship.sourceKey(), relationship.targetKey(), source, sourceAlias,
				target, targetAlias);
		if (allowStrategy && AggregateExecutionStrategyResolver.isEligibleFor(filter, strategy)) {
			String exists = "EXISTS (SELECT 1 FROM " + SqlCompiler.quoteStorageName(target.effectiveStorageName()) + " "
					+ SqlCompiler.quoteIdentifier(targetAlias) + " WHERE " + join + ")";
			return strategy == AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT ? exists : "NOT " + exists;
		}
		String predicate = filter.predicate() == null ? null
				: writeFilter(filter.predicate(), target, targetAlias, parameters, metadata, strategy, false, counters);
		String where = predicate == null ? join : join + " AND " + predicate;
		String expression;
		switch (filter.aggregate()) {
		case COUNT ->
			expression = "(SELECT COUNT(*) FROM " + SqlCompiler.quoteStorageName(target.effectiveStorageName()) + " "
					+ SqlCompiler.quoteIdentifier(targetAlias) + " WHERE " + where + ")";
		case MIN, MAX -> {
			if (filter.field() == null)
				throw new IllegalArgumentException(filter.aggregate() + " aggregate filter requires a target field.");
			FieldMetadata field = target.effectiveFields().stream().filter(x -> x.id().equals(filter.field()))
					.findFirst().orElseThrow(() -> new IllegalArgumentException(
							"Unknown aggregate filter field '" + filter.field() + "' on '" + target.name() + "'."));
			ColumnMetadata column = resolveColumn(target, field);
			String fn = filter.aggregate() == SemanticFilterAggregate.MIN ? "MIN" : "MAX";
			expression = "(SELECT " + fn + "(" + SqlCompiler.quoteIdentifier(targetAlias) + "."
					+ SqlCompiler.quoteIdentifier(column.effectiveStorageName()) + ") FROM "
					+ SqlCompiler.quoteStorageName(target.effectiveStorageName()) + " "
					+ SqlCompiler.quoteIdentifier(targetAlias) + " WHERE " + where + ")";
		}
		default -> throw new UnsupportedOperationException(filter.aggregate().toString());
		}
		String name = "p" + counters[0]++;
		Object value = filter.aggregate() == SemanticFilterAggregate.COUNT ? convertCountComparisonValue(filter.value())
				: filter.value();
		parameters.add(new SqlParameterBinding(name, value));
		return expression + renderAggregateOperator(filter.operator()) + "@" + name;
	}

	private static long convertCountComparisonValue(Object value) {
		if (value == null)
			throw new IllegalArgumentException("A Count aggregate filter requires a comparison value.");
		if (value instanceof Number n)
			return n.longValue();
		try {
			return Long.parseLong(value.toString());
		} catch (RuntimeException ex) {
			throw new IllegalArgumentException("Count aggregate filter value '" + value + "' is not an integer.", ex);
		}
	}

	private static String renderAggregateOperator(SemanticAggregateFilterOperator op) {
		return switch (op) {
		case EQ -> " = ";
		case NEQ -> " <> ";
		case GT -> " > ";
		case GTE -> " >= ";
		case LT -> " < ";
		case LTE -> " <= ";
		};
	}

	private static String comparisonOperatorSql(SemanticFilterOperator operator) {
		return switch (operator) {
		case NEQ -> " <> ";
		case GT -> " > ";
		case GTE -> " >= ";
		case LT -> " < ";
		case LTE -> " <= ";
		default -> " = ";
		};
	}

	private static String renderJoinCondition(ColumnReference sourceRef, ColumnReference targetRef,
			EntityMetadata source, String sourceAlias, EntityMetadata target, String targetAlias) {
		return renderReference(sourceRef, source, sourceAlias, target, targetAlias) + " = "
				+ renderReference(targetRef, source, sourceAlias, target, targetAlias);
	}

	private static String renderReference(ColumnReference ref, EntityMetadata source, String sourceAlias,
			EntityMetadata target, String targetAlias) {
		EntityMetadata entity;
		String alias;
		if (ref.entityId().equals(source.entityId())) {
			entity = source;
			alias = sourceAlias;
		} else if (ref.entityId().equals(target.entityId())) {
			entity = target;
			alias = targetAlias;
		} else
			throw new IllegalArgumentException("Relationship join references an entity outside its endpoints.");
		ColumnMetadata column = entity.columns().stream().filter(x -> x.id().equals(ref.columnId())).findFirst()
				.orElseThrow(() -> new IllegalArgumentException(
						"Entity '" + entity.name() + "' has no column '" + ref.columnId() + "'."));
		return SqlCompiler.quoteIdentifier(alias) + "." + SqlCompiler.quoteIdentifier(column.effectiveStorageName());
	}

	private static String join(List<SemanticFilterExpression> expressions, String op, EntityMetadata entity,
			String alias, Collection<SqlParameterBinding> parameters, IMetadataProvider metadata,
			AggregateExecutionStrategy strategy, boolean allowStrategy, int[] counters) {
		if (expressions.isEmpty())
			throw new IllegalArgumentException("Filter group cannot be empty.");
		List<String> parts = new ArrayList<>();
		for (SemanticFilterExpression e : expressions)
			parts.add(writeFilter(e, entity, alias, parameters, metadata, strategy, allowStrategy, counters));
		return "(" + String.join(" " + op + " ", parts) + ")";
	}

	private static List<Object> normalizeList(Object value) {
		if (value == null)
			return List.of();
		if (value instanceof Collection<?> c)
			return new ArrayList<>(c);
		if (value instanceof Iterable<?> it) {
			List<Object> r = new ArrayList<>();
			it.forEach(r::add);
			return r;
		}
		if (value.getClass().isArray()) {
			List<Object> r = new ArrayList<>();
			int n = Array.getLength(value);
			for (int i = 0; i < n; i++)
				r.add(Array.get(value, i));
			return r;
		}
		return List.of(value);
	}

	private static ColumnMetadata resolveColumn(EntityMetadata entity, FieldMetadata field) {
		if (field.column() == null)
			throw new IllegalArgumentException(
					"Field '" + entity.name() + "." + field.name() + "' has no storage column mapping.");
		return entity.columns().stream().filter(x -> x.id().equals(field.column().columnId())).findFirst()
				.orElseThrow(() -> new IllegalArgumentException(
						"Field '" + entity.name() + "." + field.name() + "' references a missing column."));
	}
}
