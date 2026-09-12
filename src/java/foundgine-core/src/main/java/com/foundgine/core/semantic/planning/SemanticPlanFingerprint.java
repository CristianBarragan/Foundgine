package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.query.SemanticAggregateFilter;
import com.foundgine.core.semantic.query.SemanticAndFilter;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.query.SemanticOrFilter;
import com.foundgine.core.semantic.query.SemanticOrderTerm;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.core.semantic.query.SemanticRelationshipFilter;
import com.foundgine.core.abstractions.AuthorizationPredicate;
import java.util.Comparator;
import java.util.HexFormat;
import java.util.List;
import java.util.Objects;

/**
 *
 * <p>
 * Produces a deterministic key for an execution plan. The complete authorized
 * plan is represented, including authorization predicates and request values.
 * This intentionally keys exact plans rather than pretending that arbitrary
 * filter values can safely share a compiled provider plan.
 */
public final class SemanticPlanFingerprint {

	private SemanticPlanFingerprint() {
	}

	public static String create(SemanticPlan plan) {
		Objects.requireNonNull(plan, "plan");

		StringBuilder builder = new StringBuilder(512);
		builder.append("plan-v2|");
		appendSecurityInvariants(builder, plan.requiredSecurityInvariants());
		appendNode(builder, plan.root(), true);
		return builder.toString();
	}

	/**
	 * Creates a cache key for the static query shape. Pagination values are
	 * deliberately excluded because the SQL provider binds LIMIT/OFFSET at
	 * execution time. Filters, ordering and authorization remain part of the key so
	 * unrelated query shapes do not share a provider plan.
	 */
	public static String createShapeKey(SemanticPlan plan) {
		Objects.requireNonNull(plan, "plan");

		StringBuilder builder = new StringBuilder(512);
		builder.append("plan-v2|");
		appendSecurityInvariants(builder, plan.requiredSecurityInvariants());
		appendNode(builder, plan.root(), false);
		return builder.toString();
	}

	private static void appendSecurityInvariants(StringBuilder builder, List<String> invariants) {
		builder.append("security[");

		if (invariants != null) {
			invariants.stream().sorted(Comparator.naturalOrder())
					.forEach(invariant -> builder.append(invariant).append(','));
		}

		builder.append(']');
	}

	private static void appendNode(StringBuilder builder, SemanticPlanNode node, boolean includePaginationValues) {
		builder.append("node(").append(node.id()).append('|').append((byte) node.operation().ordinal()).append('|')
				.append(node.entityId().value()).append('|')
				.append(node.viaRelationship() != null ? node.viaRelationship().value() : "-").append('|')
				.append(node.viaConnection() != null ? node.viaConnection().value() : "-").append('|')
				.append(node.relationshipCardinality() != null ? node.relationshipCardinality().toString() : "-")
				.append('|').append((byte) node.traversalMode().ordinal()).append('|').append(node.traversalOrder())
				.append('|').append((byte) node.aggregateExecutionStrategy().ordinal()).append(')');

		builder.append("fields[");
		for (var field : node.fields()) {
			builder.append(field.value()).append(',');
		}
		builder.append(']');

		appendQueryOptions(builder, node.queryOptions(), includePaginationValues);
		appendPredicate(builder, node.authorization());

		builder.append("children[");
		for (var child : node.children()) {
			appendNode(builder, child, includePaginationValues);
		}
		builder.append(']');
	}

	private static void appendQueryOptions(StringBuilder builder, SemanticQueryOptions options,
			boolean includePaginationValues) {
		if (options == null) {
			builder.append("query[-]");
			return;
		}

		builder.append("query[");
		if (includePaginationValues) {
			builder.append(options.limit() != null ? options.limit().toString() : "-").append('|')
					.append(options.offset() != null ? options.offset().toString() : "-").append('|');
			appendValue(builder, options.after());
		} else {
			builder.append("pagination-parameterized|");
		}

		builder.append('|');

		builder.append("order[");
		for (SemanticOrderTerm term : options.effectiveOrder()) {
			builder.append(term.field().value()).append(':').append((byte) term.direction().ordinal()).append(':')
					.append((byte) term.aggregate().ordinal()).append(':');
			for (var relationship : term.effectivePath()) {
				builder.append(relationship.value()).append('.');
			}
			builder.append(',');
		}

		builder.append("]|");
		appendFilter(builder, options.filter());
		builder.append(']');
	}

	private static void appendFilter(StringBuilder builder, SemanticFilterExpression filter) {
		switch (filter) {
		case null -> builder.append("filter[-]");
		case SemanticFieldFilter field -> {
			builder.append("field(").append(field.field().value()).append('|').append((byte) field.operator().ordinal())
					.append('|');
			appendValue(builder, field.value());
			builder.append(')');
		}
		case SemanticRelationshipFilter relationship -> {
			builder.append("relationship(").append(relationship.relationship().value()).append('|')
					.append((byte) relationship.quantifier().ordinal()).append('|');
			appendFilter(builder, relationship.predicate());
			builder.append(')');
		}
		case SemanticAggregateFilter aggregate -> {
			builder.append("aggregate(").append(aggregate.relationship().value()).append('|')
					.append((byte) aggregate.aggregate().ordinal()).append('|')
					.append(aggregate.field() != null ? String.valueOf(aggregate.field().value()) : "-").append('|')
					.append((byte) aggregate.operator().ordinal()).append('|');
			appendValue(builder, aggregate.value());
			builder.append('|');
			appendFilter(builder, aggregate.predicate());
			builder.append(')');
		}
		case SemanticAndFilter and -> {
			builder.append("and[");
			for (var expression : and.expressions()) {
				appendFilter(builder, expression);
			}
			builder.append(']');
		}
		case SemanticOrFilter or -> {
			builder.append("or[");
			for (var expression : or.expressions()) {
				appendFilter(builder, expression);
			}
			builder.append(']');
		}
		default -> throw new UnsupportedOperationException(
				"Cannot fingerprint filter '" + filter.getClass().getSimpleName() + "'.");
		}
	}

	private static void appendPredicate(StringBuilder builder, AuthorizationPredicate predicate) {
		builder.append("auth[");
		if (predicate != null) {
			appendPredicateNode(builder, predicate);
		}
		builder.append(']');
	}

	private static void appendPredicateNode(StringBuilder builder, AuthorizationPredicate node) {
		builder.append((byte) node.kind().ordinal()).append('(');
		appendValue(builder, node.name());
		builder.append('|');
		appendValue(builder, node.value());
		builder.append('|');
		if (node.left() != null) {
			appendPredicateNode(builder, node.left());
		}
		builder.append('|');
		if (node.right() != null) {
			appendPredicateNode(builder, node.right());
		}
		builder.append(')');
	}

	/**
	 * Appends a canonical, type-discriminated representation of an arbitrary
	 * filter/predicate value, prefixed with the runtime class's fully-qualified
	 * name, which serves the purpose of preventing values of different types from
	 * colliding under the same textual form.
	 */
	private static void appendValue(StringBuilder builder, Object value) {
		if (value == null) {
			builder.append("null");
			return;
		}

		builder.append(value.getClass().getName()).append('=');

		switch (value) {
		case String text -> builder.append(text.length()).append(':').append(text);
		case byte[] bytes -> builder.append(HexFormat.of().withUpperCase().formatHex(bytes));
		case Iterable<?> sequence -> {
			builder.append('[');
			for (Object item : sequence) {
				appendValue(builder, item);
				builder.append(',');
			}
			builder.append(']');
		}
		default -> builder.append(value);
		}
	}
}