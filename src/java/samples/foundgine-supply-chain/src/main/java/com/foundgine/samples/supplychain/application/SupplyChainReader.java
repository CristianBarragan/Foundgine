package com.foundgine.samples.supplychain.application;

import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.semantic.configuration.PropertyRef;
import com.foundgine.core.semantic.ir.SemanticQuery;
import com.foundgine.core.semantic.query.SemanticSortDirection;
import com.foundgine.providers.storage.orm.jpa.FoundgineJpaReader;
import com.foundgine.samples.supplychain.Authorization;

import java.lang.invoke.SerializedLambda;
import java.util.ArrayList;
import java.util.List;
import java.util.function.Consumer;

/**
 * Semantic reads written with method references, the Java twin of the C# lambda reader:
 * {@code reader.from(Supplier.class).eq(Supplier::country, "US").select(Supplier::id, Supplier::name).rows()}.
 *
 * <p>Java lambdas expose no expression trees, so only getter/record-accessor method references are accepted and
 * each is turned into a semantic field name (matched case-insensitively by the planner). Every read still goes
 * through authorize -> plan -> ExecutionIR -> JPA, so the caller's tenant predicate and field rules apply.
 */
public final class SupplyChainReader {
    private final FoundgineJpaReader<Authorization.Context> reader;
    private final Authorization.Context caller;

    public SupplyChainReader(FoundgineJpaReader<Authorization.Context> reader, Authorization.Context caller) {
        this.reader = reader;
        this.caller = caller;
    }

    /** Starts a read of the semantic entity named after the domain type ({@code Domain.Supplier -> "Supplier"}). */
    public <T> Query<T> from(Class<T> domainType) {
        return new Query<>(domainType.getSimpleName());
    }

    public final class Query<T> {
        private final String entity;
        private final List<Consumer<SemanticQuery>> steps = new ArrayList<>();
        private final List<String> selected = new ArrayList<>();

        private Query(String entity) {
            this.entity = entity;
        }

        public Query<T> eq(PropertyRef<T, ?> property, Object value) {
            var name = fieldName(property);
            steps.add(q -> q.whereEq(name, value));
            return this;
        }

        public Query<T> neq(PropertyRef<T, ?> property, Object value) {
            var name = fieldName(property);
            steps.add(q -> q.whereNeq(name, value));
            return this;
        }

        public Query<T> in(PropertyRef<T, ?> property, Object... values) {
            var name = fieldName(property);
            steps.add(q -> q.whereIn(name, values));
            return this;
        }

        @SafeVarargs
        public final Query<T> select(PropertyRef<T, ?>... properties) {
            for (var p : properties) selected.add(fieldName(p));
            return this;
        }

        public Query<T> orderBy(PropertyRef<T, ?> property, SemanticSortDirection direction) {
            var name = fieldName(property);
            steps.add(q -> q.orderBy(name, direction));
            return this;
        }

        public Query<T> take(int count) {
            steps.add(q -> q.take(count));
            return this;
        }

        public List<ExecutionRow> rows() {
            return reader.read(caller, entity, q -> {
                if (!selected.isEmpty()) q.select(selected);
                steps.forEach(s -> s.accept(q));
            });
        }
    }

    /** The semantic field name behind a getter or record accessor reference ({@code Supplier::country -> country}). */
    static String fieldName(PropertyRef<?, ?> reference) {
        try {
            var writeReplace = reference.getClass().getDeclaredMethod("writeReplace");
            writeReplace.setAccessible(true);
            var lambda = (SerializedLambda) writeReplace.invoke(reference);
            var method = lambda.getImplMethodName();
            if (method.startsWith("lambda$")) {
                throw new IllegalArgumentException("Use a method reference such as Supplier::country, not a lambda.");
            }
            if (method.length() > 3 && method.startsWith("get") && Character.isUpperCase(method.charAt(3))) {
                return Character.toLowerCase(method.charAt(3)) + method.substring(4);
            }
            if (method.length() > 2 && method.startsWith("is") && Character.isUpperCase(method.charAt(2))) {
                return Character.toLowerCase(method.charAt(2)) + method.substring(3);
            }
            return method;
        } catch (ReflectiveOperationException e) {
            throw new IllegalArgumentException("Cannot read the property name from the reference.", e);
        }
    }
}
