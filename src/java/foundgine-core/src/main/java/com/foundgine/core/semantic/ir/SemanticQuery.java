package com.foundgine.core.semantic.ir;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.query.SemanticAndFilter;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticOrderTerm;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.core.semantic.query.SemanticSortDirection;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.function.Consumer;
import java.util.stream.StreamSupport;

/**
 * Fluent builder for semantic reads. It only assembles the core IR ({@link SemanticOperation}); authorization,
 * planning and execution are unchanged. Defaults: all fields of the entity; an included relationship resolves its
 * target entity from the metadata and selects all of its fields. Fields may be given as ids or by name
 * (case-insensitive). The C# twin is {@code Foundgine.Core.Semantic.IR.SemanticQuery} with the same members in
 * PascalCase ({@code From, Where, WhereEq, OrderBy, Select, Take, Skip, Include, Build}).
 *
 * <pre>{@code
 * var op = SemanticQuery.from(metadata, "Warehouse")
 *         .select("Id", "Name")
 *         .whereEq("Region", "NZ")
 *         .include(inventoryRelationship, lots -> lots.select("Sku", "OnHand"))
 *         .build();
 * }</pre>
 */
public final class SemanticQuery {
    private final MetadataRegistry metadata;
    private final EntityMetadata entity;
    private final RelationshipId via;
    private final List<SemanticFilterExpression> filters = new ArrayList<>();
    private final List<SemanticOrderTerm> order = new ArrayList<>();
    private final List<SemanticQuery> includes = new ArrayList<>();
    private List<FieldId> fields;
    private Integer limit;
    private Integer offset;

    private SemanticQuery(MetadataRegistry metadata, EntityMetadata entity, RelationshipId via) {
        this.metadata = Objects.requireNonNull(metadata);
        this.entity = Objects.requireNonNull(entity);
        this.via = via;
    }

    public static SemanticQuery from(MetadataRegistry metadata, EntityId entity) {
        return new SemanticQuery(metadata, metadata.get(entity), null);
    }

    public static SemanticQuery from(MetadataRegistry metadata, String entityName) {
        var found = StreamSupport.stream(metadata.entities().spliterator(), false)
                .filter(e -> e.name().equals(entityName)).findFirst()
                .orElseThrow(() -> new IllegalArgumentException("Unknown entity " + entityName));
        return new SemanticQuery(metadata, found, null);
    }

    /** Adds conditions; several calls and several arguments are combined with AND. */
    public SemanticQuery where(SemanticFilterExpression... conditions) {
        filters.addAll(List.of(conditions));
        return this;
    }

    public SemanticQuery whereEq(FieldId field, Object value) {
        return where(new SemanticFieldFilter(field, SemanticFilterOperator.EQ, value));
    }

    public SemanticQuery whereEq(String field, Object value) {
        return whereEq(fieldId(field), value);
    }

    public SemanticQuery whereNeq(FieldId field, Object value) {
        return where(new SemanticFieldFilter(field, SemanticFilterOperator.NEQ, value));
    }

    public SemanticQuery whereNeq(String field, Object value) {
        return whereNeq(fieldId(field), value);
    }

    public SemanticQuery whereIn(FieldId field, Object... values) {
        return where(new SemanticFieldFilter(field, SemanticFilterOperator.IN, java.util.Arrays.asList(values)));
    }

    public SemanticQuery whereIn(String field, Object... values) {
        return whereIn(fieldId(field), values);
    }

    public SemanticQuery orderBy(SemanticOrderTerm... terms) {
        order.addAll(List.of(terms));
        return this;
    }

    public SemanticQuery orderBy(String field, SemanticSortDirection direction) {
        order.add(new SemanticOrderTerm(fieldId(field), direction));
        return this;
    }

    public SemanticQuery select(FieldId... selected) {
        fields = List.of(selected);
        return this;
    }

    public SemanticQuery select(String... names) {
        fields = List.of(names).stream().map(this::fieldId).toList();
        return this;
    }

    public SemanticQuery select(List<String> names) {
        return select(names.toArray(String[]::new));
    }

    public SemanticQuery take(int value) {
        limit = value;
        return this;
    }

    public SemanticQuery skip(int value) {
        offset = value;
        return this;
    }

    /** Loads a related collection; the lambda (optional) refines the child query. */
    public SemanticQuery include(RelationshipId relationship, Consumer<SemanticQuery> configure) {
        var rel = metadata.get(relationship);
        var child = new SemanticQuery(metadata, metadata.get(rel.target()), relationship);
        if (configure != null) configure.accept(child);
        includes.add(child);
        return this;
    }

    public SemanticQuery include(RelationshipId relationship) {
        return include(relationship, null);
    }

    public SemanticOperation build() {
        return new SemanticOperation(toNode(new int[] {1}));
    }

    private SemanticReadNode toNode(int[] next) {
        int id = next[0]++;
        var children = new ArrayList<SemanticReadNode>();
        for (var include : includes) children.add(include.toNode(next));
        SemanticFilterExpression filter = switch (filters.size()) {
            case 0 -> null;
            case 1 -> filters.get(0);
            default -> new SemanticAndFilter(List.copyOf(filters));
        };
        var options = filter == null && order.isEmpty() && limit == null && offset == null
                ? null
                : new SemanticQueryOptions(filter, order, limit, offset, null);
        var selected = fields != null
                ? fields
                : entity.effectiveFields().stream().map(f -> f.id()).toList();
        return new SemanticReadNode(id, entity.entityId(), selected, via, null, children, options, null);
    }

    private FieldId fieldId(String name) {
        return entity.effectiveFields().stream().filter(f -> f.name().equalsIgnoreCase(name)).findFirst()
                .orElseThrow(() -> new IllegalArgumentException("Unknown field " + entity.name() + "." + name)).id();
    }
}
