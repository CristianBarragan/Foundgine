package com.foundgine.samples.comparison.spring;

import com.foundgine.samples.comparison.spring.Entities.*;
import jakarta.persistence.criteria.AbstractQuery;
import jakarta.persistence.criteria.CriteriaBuilder;
import jakarta.persistence.criteria.Expression;
import jakarta.persistence.criteria.From;
import jakarta.persistence.criteria.Order;
import jakarta.persistence.criteria.Path;
import jakarta.persistence.criteria.Predicate;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * Translates the hand-declared GraphQL {@code where} / {@code order} inputs into JPA Criteria. Spring for GraphQL has no
 * filter generation, so this translator and the matching input types in schema.graphqls are application code.
 */
final class Filters {
    private Filters() {}

    /** A filterable type: GraphQL field -> JPA attribute, plus collection fields reached through a child foreign key. */
    record Shape(Class<?> type, Map<String, String> scalars, Map<String, Rel> collections) {}

    record Rel(Shape child, String childKey) {}

    static final Shape PRODUCT = new Shape(ProductRow.class,
            Map.of("id", "id", "sku", "sku", "name", "name", "price", "price"), Map.of());
    static final Shape LINE = new Shape(OrderLineRow.class,
            Map.of("id", "id", "orderId", "orderId", "productId", "productId", "quantity", "quantity", "unitPrice", "unitPrice"),
            Map.of());
    static final Shape ORDER = new Shape(OrderRow.class,
            Map.of("id", "id", "customerId", "customerId", "status", "status", "total", "total"),
            Map.of("lines", new Rel(LINE, "orderId")));
    static final Shape CUSTOMER = new Shape(CustomerRow.class,
            Map.of("id", "id", "firstName", "firstName", "lastName", "lastName", "email", "email"),
            Map.of("orders", new Rel(ORDER, "customerId")));

    @SuppressWarnings("unchecked")
    static Predicate where(Shape s, Map<String, Object> w, From<?, ?> from, AbstractQuery<?> q, CriteriaBuilder cb) {
        var ps = new ArrayList<Predicate>();
        for (var e : w.entrySet()) {
            String key = e.getKey();
            Object value = e.getValue();
            if (key.equals("and") || key.equals("or")) {
                var parts = ((List<Map<String, Object>>) value).stream().map(x -> where(s, x, from, q, cb)).toArray(Predicate[]::new);
                ps.add(key.equals("and") ? cb.and(parts) : cb.or(parts));
            } else if (s.collections().containsKey(key)) {
                var rel = s.collections().get(key);
                for (var q2 : ((Map<String, Map<String, Object>>) value).entrySet()) {
                    var sub = q.subquery(Integer.class);
                    var child = sub.from(rel.child().type());
                    sub.select(cb.literal(1)).where(
                            cb.equal(child.get(rel.childKey()), from.get("id")),
                            where(rel.child(), q2.getValue(), child, sub, cb));
                    ps.add(q2.getKey().equals("some") ? cb.exists(sub) : cb.not(cb.exists(sub)));
                }
            } else if (s.scalars().containsKey(key)) {
                ps.add(operators(from.get(s.scalars().get(key)), (Map<String, Object>) value, cb));
            } else {
                throw new IllegalArgumentException("Unknown filter field '" + key + "'.");
            }
        }
        return cb.and(ps.toArray(Predicate[]::new));
    }

    @SuppressWarnings({"unchecked", "rawtypes"})
    private static Predicate operators(Path<?> path, Map<String, Object> ops, CriteriaBuilder cb) {
        var out = new ArrayList<Predicate>();
        Class<?> type = path.getJavaType();
        Expression<Comparable> x = (Expression) path;
        for (var e : ops.entrySet()) {
            Object v = e.getValue();
            switch (e.getKey()) {
                case "eq" -> out.add(cb.equal(path, coerce(type, v)));
                case "neq" -> out.add(cb.notEqual(path, coerce(type, v)));
                case "gt" -> out.add(cb.greaterThan(x, (Comparable) coerce(type, v)));
                case "gte" -> out.add(cb.greaterThanOrEqualTo(x, (Comparable) coerce(type, v)));
                case "lt" -> out.add(cb.lessThan(x, (Comparable) coerce(type, v)));
                case "lte" -> out.add(cb.lessThanOrEqualTo(x, (Comparable) coerce(type, v)));
                case "contains" -> out.add(cb.like(path.as(String.class), "%" + v + "%"));
                case "in" -> out.add(path.in(((List<?>) v).stream().map(i -> coerce(type, i)).toList()));
                default -> throw new IllegalArgumentException("Unknown operator '" + e.getKey() + "'.");
            }
        }
        return cb.and(out.toArray(Predicate[]::new));
    }

    /** GraphQL Float arrives as Double and Int as Integer; JPA compares against the attribute's own type. */
    private static Object coerce(Class<?> type, Object v) {
        if (v == null) return null;
        if (type == BigDecimal.class) return new BigDecimal(v.toString());
        if (type == Integer.class) return ((Number) v).intValue();
        return v;
    }

    static List<Order> orders(Shape s, Map<String, Object> order, From<?, ?> from, CriteriaBuilder cb) {
        var out = new ArrayList<Order>();
        for (var e : order.entrySet()) {
            var path = from.get(s.scalars().get(e.getKey()));
            out.add("DESC".equals(String.valueOf(e.getValue())) ? cb.desc(path) : cb.asc(path));
        }
        return out;
    }
}
