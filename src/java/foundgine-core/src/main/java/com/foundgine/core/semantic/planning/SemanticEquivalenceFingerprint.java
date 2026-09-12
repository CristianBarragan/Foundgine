package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.AuthorizationPredicateKind;
import com.foundgine.core.semantic.query.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

/**
 * Produces a canonical semantic identity used to determine whether two plans
 * have the same provider-neutral meaning. Unlike the execution fingerprint,
 * this representation normalizes only transformations defined as semantically
 * equivalent.
 */
public final class SemanticEquivalenceFingerprint {
	private SemanticEquivalenceFingerprint() {
	}

	public static String create(SemanticPlan plan) {
		Objects.requireNonNull(plan, "plan");
		StringBuilder b = new StringBuilder(512);
		b.append("semantic-v1|");
		b.append("security[");
		plan.effectiveSecurityInvariants().stream().sorted().forEach(x -> b.append(x).append(','));
		b.append("]|");
		appendNode(b, plan.root());
		return b.toString();
	}

	private static void appendNode(StringBuilder b, SemanticPlanNode node) {
		b.append("node(").append(node.id()).append('|').append((byte) node.operation().ordinal()).append('|')
				.append(node.entityId().value()).append('|')
				.append(node.viaRelationship() != null ? node.viaRelationship().value() : "-").append('|')
				.append(node.viaConnection() != null ? node.viaConnection().value() : "-").append(')');
		b.append("fields[");
		Set<Long> seen = new HashSet<>();
		for (var f : node.fields())
			if (seen.add(f.value()))
				b.append(f.value()).append(',');
		b.append("]|");
		appendQueryOptions(b, node.queryOptions());
		b.append("auth[");
		appendAuthorization(b, node.authorization());
		b.append("]|");
		b.append("children[");
		node.children().forEach(c -> appendNode(b, c));
		b.append(']');
	}

	private static void appendQueryOptions(StringBuilder b, SemanticQueryOptions options) {
		if (options == null) {
			b.append("query[-]|");
			return;
		}
		b.append("query[").append(options.limit() != null ? options.limit() : "-").append('|')
				.append(options.offset() != null ? options.offset() : "-").append('|');
		appendValue(b, options.after());
		b.append("|order[");
		for (var term : options.effectiveOrder()) {
			b.append(term.field().value()).append(':').append((byte) term.direction().ordinal()).append(':')
					.append((byte) term.aggregate().ordinal()).append(':');
			for (var r : term.effectivePath())
				b.append(r.value()).append('.');
			b.append(',');
		}
		b.append("]|");
		appendFilter(b, options.filter());
		b.append("]|");
	}

	private static void appendFilter(StringBuilder b, SemanticFilterExpression filter) {
		filter = canonicalizeAggregateRelationshipPushdown(filter);
		if (filter == null) {
			b.append('-');
			return;
		}
		if (filter instanceof SemanticFieldFilter f) {
			b.append("field(").append(f.field().value()).append('|').append((byte) f.operator().ordinal()).append('|');
			appendValue(b, f.value());
			b.append(')');
			return;
		}
		if (filter instanceof SemanticRelationshipFilter r) {
			b.append("relationship(").append(r.relationship().value()).append('|')
					.append((byte) r.quantifier().ordinal()).append('|');
			appendFilter(b, r.predicate());
			b.append(')');
			return;
		}
		if (filter instanceof SemanticAggregateFilter a) {
			b.append("aggregate(").append(a.relationship().value()).append('|').append((byte) a.aggregate().ordinal())
					.append('|').append(a.field() != null ? a.field().value() : "-").append('|')
					.append((byte) a.operator().ordinal()).append('|');
			appendValue(b, a.value());
			b.append('|');
			appendFilter(b, a.predicate());
			b.append(')');
			return;
		}
		if (filter instanceof SemanticAndFilter a) {
			appendCanonicalBooleanFilter(b, a);
			return;
		}
		if (filter instanceof SemanticOrFilter o) {
			appendCanonicalBooleanFilter(b, o);
			return;
		}
		throw new UnsupportedOperationException(
				"Cannot establish semantic equivalence for filter '" + filter.getClass().getSimpleName() + "'.");
	}

	private static void appendCanonicalBooleanFilter(StringBuilder b, SemanticFilterExpression filter) {
		var terms = toDnf(filter, 32);
		if (terms == null) {
			if (filter instanceof SemanticAndFilter a)
				appendCommutativeFilter(b, "and", a.expressions());
			else if (filter instanceof SemanticOrFilter o)
				appendCommutativeFilter(b, "or", o.expressions());
			else
				throw new IllegalStateException("Expected boolean filter.");
			return;
		}
		b.append("dnf[");
		var normalized = new ArrayList<List<String>>();
		for (var term : terms) {
			var keys = term.stream().map(SemanticEquivalenceFingerprint::createFilterKey).sorted().toList();
			normalized.add(keys);
		}
		normalized.sort(Comparator.comparing(x -> String.join("&", x)));
		for (var term : normalized) {
			b.append('(');
			term.forEach(x -> b.append(x).append(','));
			b.append(')');
		}
		b.append(']');
	}

	private static SemanticFilterExpression canonicalizeAggregateRelationshipPushdown(SemanticFilterExpression filter) {
		if (filter == null)
			return null;
		if (filter instanceof SemanticAndFilter a) {
			var raw = a.expressions();
			for (int i = 0; i < raw.size(); i++) {
				if (!(raw.get(i) instanceof SemanticAggregateFilter aggregate)
						|| aggregate.aggregate() != SemanticFilterAggregate.COUNT || aggregate.field() != null
						|| aggregate.predicate() != null || !isCountExistenceComparison(aggregate))
					continue;
				for (int j = 0; j < raw.size(); j++) {
					if (i == j || !(raw.get(j) instanceof SemanticRelationshipFilter r)
							|| r.quantifier() != SemanticRelationshipQuantifier.SOME
							|| !r.relationship().equals(aggregate.relationship()))
						continue;
					var pushed = new SemanticAggregateFilter(aggregate.relationship(), aggregate.aggregate(),
							aggregate.field(), aggregate.operator(), aggregate.value(), r.predicate());
					var remaining = new ArrayList<SemanticFilterExpression>();
					for (int k = 0; k < raw.size(); k++) {
						if (k == i)
							remaining.add(pushed);
						else if (k != j)
							remaining.add(raw.get(k));
					}
					SemanticFilterExpression merged = remaining.size() == 1 ? remaining.get(0)
							: new SemanticAndFilter(remaining);
					return canonicalizeAggregateRelationshipPushdown(merged);
				}
			}
			var expressions = a.expressions().stream()
					.map(SemanticEquivalenceFingerprint::canonicalizeAggregateRelationshipPushdown).toList();
			return expressions.size() == 1 ? expressions.get(0) : new SemanticAndFilter(expressions);
		}
		if (filter instanceof SemanticAggregateFilter a && a.aggregate() == SemanticFilterAggregate.COUNT
				&& a.field() == null && a.predicate() != null) {
			var strategy = AggregateExecutionStrategyResolver.resolve(a.operator(), a.value());
			if (strategy == AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT
					|| strategy == AggregateExecutionStrategy.COUNT_EMPTY_SHORT_CIRCUIT)
				return new SemanticRelationshipFilter(a.relationship(),
						strategy == AggregateExecutionStrategy.COUNT_EMPTY_SHORT_CIRCUIT
								? SemanticRelationshipQuantifier.NONE
								: SemanticRelationshipQuantifier.SOME,
						canonicalizeAggregateRelationshipPushdown(a.predicate()));
			return new SemanticAggregateFilter(a.relationship(), a.aggregate(), a.field(), a.operator(), a.value(),
					canonicalizeAggregateRelationshipPushdown(a.predicate()));
		}
		if (filter instanceof SemanticOrFilter o)
			return new SemanticOrFilter(o.expressions().stream()
					.map(SemanticEquivalenceFingerprint::canonicalizeAggregateRelationshipPushdown).toList());
		if (filter instanceof SemanticRelationshipFilter r) {
			var p = canonicalizeAggregateRelationshipPushdown(r.predicate());
			return p == r.predicate() ? r : new SemanticRelationshipFilter(r.relationship(), r.quantifier(), p);
		}
		return filter;
	}

	private static boolean isCountExistenceComparison(SemanticAggregateFilter a) {
		Long v = tryGetIntegral(a.value());
		if (v == null)
			return false;
		return (a.operator() == SemanticAggregateFilterOperator.GT && v == 0)
				|| (a.operator() == SemanticAggregateFilterOperator.GTE && v == 1)
				|| (a.operator() == SemanticAggregateFilterOperator.NEQ && v == 0);
	}

	private static Long tryGetIntegral(Object value) {
		if (value instanceof Byte v)
			return v.longValue();
		if (value instanceof Short v)
			return v.longValue();
		if (value instanceof Integer v)
			return v.longValue();
		if (value instanceof Long v)
			return v;
		return null;
	}

	private static List<List<SemanticFilterExpression>> toDnf(SemanticFilterExpression e, int max) {
		if (e instanceof SemanticAndFilter a) {
			List<List<SemanticFilterExpression>> result = new ArrayList<>();
			result.add(List.of());
			for (var child : a.expressions()) {
				var childTerms = toDnf(child, max);
				if (childTerms == null)
					return null;
				var next = new ArrayList<List<SemanticFilterExpression>>();
				for (var l : result)
					for (var r : childTerms) {
						var c = new ArrayList<SemanticFilterExpression>(l);
						c.addAll(r);
						next.add(c);
						if (next.size() > max)
							return null;
					}
				result = next;
			}
			return result;
		}
		if (e instanceof SemanticOrFilter o) {
			var result = new ArrayList<List<SemanticFilterExpression>>();
			for (var child : o.expressions()) {
				var t = toDnf(child, max);
				if (t == null)
					return null;
				result.addAll(t);
				if (result.size() > max)
					return null;
			}
			return result;
		}
		return List.of(List.of(e));
	}

	private static void appendCommutativeFilter(StringBuilder b, String kind,
			List<SemanticFilterExpression> expressions) {
		b.append(kind).append('[');
		expressions.stream().map(SemanticEquivalenceFingerprint::createFilterKey).sorted()
				.forEach(x -> b.append(x).append(','));
		b.append(']');
	}

	private static String createFilterKey(SemanticFilterExpression e) {
		StringBuilder b = new StringBuilder();
		appendFilter(b, e);
		return b.toString();
	}

	private static void appendAuthorization(StringBuilder b, AuthorizationPredicate p) {
		if (p != null)
			appendAuthorizationNode(b, canonicalAuthorization(p));
	}

	private static AuthorizationPredicate canonicalAuthorization(AuthorizationPredicate p) {
		var left = p.left() == null ? null : canonicalAuthorization(p.left());
		var right = p.right() == null ? null : canonicalAuthorization(p.right());
		var current = (left == p.left() && right == p.right()) ? p
				: new AuthorizationPredicate(p.kind(), p.name(), p.value(), left, right);
		if (current.kind() == AuthorizationPredicateKind.NOT && current.left() != null
				&& current.left().kind() == AuthorizationPredicateKind.NOT && current.left().left() != null)
			return canonicalAuthorization(current.left().left());
		if (current.kind() == AuthorizationPredicateKind.AND || current.kind() == AuthorizationPredicateKind.OR) {
			var ops = new ArrayList<AuthorizationPredicate>();
			flattenAuthorization(current.kind(), current, ops);
			var unique = new LinkedHashMap<String, AuthorizationPredicate>();
			for (var op : ops) {
				var c = canonicalAuthorization(op);
				unique.putIfAbsent(createAuthorizationKey(c), c);
			}
			ops = new ArrayList<>(unique.values());
			ops.sort(Comparator.comparing(SemanticEquivalenceFingerprint::createAuthorizationKey));
			AuthorizationPredicate result = ops.get(0);
			for (int i = 1; i < ops.size(); i++)
				result = current.kind() == AuthorizationPredicateKind.AND
						? AuthorizationPredicate.and(result, ops.get(i))
						: AuthorizationPredicate.or(result, ops.get(i));
			return result;
		}
		return current;
	}

	private static void flattenAuthorization(AuthorizationPredicateKind kind, AuthorizationPredicate n,
			List<AuthorizationPredicate> out) {
		if (n.kind() == kind) {
			if (n.left() != null)
				flattenAuthorization(kind, n.left(), out);
			if (n.right() != null)
				flattenAuthorization(kind, n.right(), out);
		} else
			out.add(n);
	}

	private static String createAuthorizationKey(AuthorizationPredicate p) {
		StringBuilder b = new StringBuilder();
		appendAuthorizationNode(b, p);
		return b.toString();
	}

	private static void appendAuthorizationNode(StringBuilder b, AuthorizationPredicate p) {
		b.append((byte) p.kind().ordinal()).append('(');
		appendValue(b, p.name());
		b.append('|');
		appendValue(b, p.value());
		b.append('|');
		if (p.left() != null)
			appendAuthorizationNode(b, p.left());
		b.append('|');
		if (p.right() != null)
			appendAuthorizationNode(b, p.right());
		b.append(')');
	}

	private static void appendValue(StringBuilder b, Object value) {
		if (value == null) {
			b.append("null");
			return;
		}
		b.append(value.getClass().getName()).append('=');
		if (value instanceof String s)
			b.append(s.length()).append(':').append(s);
		else if (value instanceof byte[] bytes) {
			for (byte x : bytes)
				b.append(String.format("%02X", x));
		} else if (value instanceof Iterable<?> it) {
			b.append('[');
			for (var x : it) {
				appendValue(b, x);
				b.append(',');
			}
			b.append(']');
		} else
			b.append(value);
	}
}
