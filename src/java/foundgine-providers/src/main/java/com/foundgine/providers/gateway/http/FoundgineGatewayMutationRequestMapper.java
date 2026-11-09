package com.foundgine.providers.gateway.http;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.mutation.SemanticMutationBuilder;
import com.foundgine.core.semantic.mutation.SemanticMutationField;
import com.foundgine.core.semantic.mutation.SemanticMutationKind;
import com.foundgine.core.semantic.mutation.SemanticMutationOperation;
import com.foundgine.core.semantic.mutation.SemanticMutationOperationGraph;
import com.foundgine.core.semantic.query.SemanticAndFilter;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticOrFilter;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;

/** Validates a wire mutation request against the HTTP limits and maps it to the canonical mutation graph. */
public final class FoundgineGatewayMutationRequestMapper {
    private FoundgineGatewayMutationRequestMapper() { }

    private static final class Counter { int value; }

    public static SemanticMutationOperationGraph toGraph(FoundgineGatewayMutationRequest request, FoundgineGatewayHttpOptions limits) {
        if (request == null) throw new IllegalArgumentException("request is required");
        var operations = request.operations();
        if (operations == null || operations.isEmpty() || operations.size() > limits.maxMutationOperations)
            throw new IllegalArgumentException("Mutation operations must be non-empty and within the configured limit.");
        var mapped = new ArrayList<SemanticMutationOperation>(operations.size());
        for (var operation : operations) {
            if (operation == null || operation.entityId() == 0) throw new IllegalArgumentException("Every mutation requires a non-zero entityId.");
            var fields = new ArrayList<SemanticMutationField>();
            var seen = new HashSet<Long>();
            if (operation.fields() != null) {
                if (operation.fields().size() > limits.maxMutationFields) throw new IllegalArgumentException("Mutation field limit exceeded.");
                for (var pair : operation.fields().entrySet()) {
                    long fieldId = parseFieldKey(pair.getKey());
                    if (!seen.add(fieldId)) throw new IllegalArgumentException("Mutation field IDs must be unique.");
                    fields.add(new SemanticMutationField(new FieldId(fieldId), GatewayJsonValues.normalize(pair.getValue(), 0, limits.maxJsonValueDepth, "mutation")));
                }
            }
            var entity = new EntityId(operation.entityId());
            var returns = fieldIds(operation.returnFields(), "Return");
            var conflicts = fieldIds(operation.conflictFields(), "Conflict");
            var kind = GatewayJsonValues.parseEnum(SemanticMutationKind.class, operation.kind(), "mutation kind");
            if (returns.size() > limits.maxMutationFields || conflicts.size() > limits.maxMutationFields)
                throw new IllegalArgumentException("Mutation return/conflict field limit exceeded.");
            var filterNodes = new Counter();
            SemanticFilterExpression filter = operation.filter() == null ? null : mapFilter(operation.filter(), 1, filterNodes, limits);
            String kindName = kind.name().charAt(0) + kind.name().substring(1).toLowerCase(Locale.ROOT);
            if ((kind == SemanticMutationKind.UPDATE || kind == SemanticMutationKind.DELETE) && filter == null)
                throw new IllegalArgumentException(kindName + " mutations require a target filter.");
            if (kind == SemanticMutationKind.UPSERT && conflicts.isEmpty())
                throw new IllegalArgumentException("Upsert mutations require conflictFields.");
            if ((kind == SemanticMutationKind.CREATE || kind == SemanticMutationKind.UPSERT) && filter != null)
                throw new IllegalArgumentException(kindName + " mutations do not accept a target filter.");
            if (kind != SemanticMutationKind.UPSERT && !conflicts.isEmpty())
                throw new IllegalArgumentException("conflictFields are supported only for Upsert.");
            if (kind == SemanticMutationKind.DELETE && !fields.isEmpty())
                throw new IllegalArgumentException("Delete mutations do not accept field assignments.");
            mapped.add(switch (kind) {
                case CREATE -> SemanticMutationBuilder.create(entity, fields, returns);
                case UPDATE -> SemanticMutationBuilder.update(entity, fields, filter, returns);
                case DELETE -> SemanticMutationBuilder.delete(entity, filter, returns);
                case UPSERT -> SemanticMutationBuilder.upsert(entity, fields, conflicts, returns);
            });
        }
        return new SemanticMutationOperationGraph(mapped);
    }

    private static long parseFieldKey(String key) {
        if (key == null || key.isEmpty() || !key.chars().allMatch(c -> c >= '0' && c <= '9'))
            throw new IllegalArgumentException("Mutation field keys must be non-zero numeric semantic field IDs.");
        long id;
        try { id = Long.parseUnsignedLong(key); }
        catch (NumberFormatException e) { throw new IllegalArgumentException("Mutation field keys must be non-zero numeric semantic field IDs."); }
        if (id == 0) throw new IllegalArgumentException("Mutation field keys must be non-zero numeric semantic field IDs.");
        return id;
    }

    private static List<FieldId> fieldIds(List<Long> ids, String label) {
        var out = new ArrayList<FieldId>();
        if (ids != null) for (long id : ids) {
            if (id == 0) throw new IllegalArgumentException(label + " field IDs must be non-zero.");
            out.add(new FieldId(id));
        }
        return out;
    }

    private static SemanticFilterExpression mapFilter(FoundgineGatewayMutationRequest.Filter filter, int depth, Counter count,
            FoundgineGatewayHttpOptions limits) {
        if (filter == null) throw new IllegalArgumentException("Filter entries cannot be null.");
        if (depth > limits.maxMutationFilterDepth || ++count.value > limits.maxMutationFilterNodes)
            throw new IllegalArgumentException("Mutation filter depth/node limit exceeded.");
        String kind = filter.kind() == null ? "" : filter.kind().trim().toLowerCase(Locale.ROOT);
        if (kind.equals("field") && filter.fieldId() != null && filter.fieldId() != 0)
            return new SemanticFieldFilter(new FieldId(filter.fieldId()),
                    GatewayJsonValues.parseEnum(SemanticFilterOperator.class, filter.operator(), "filter operator"),
                    GatewayJsonValues.normalize(filter.value(), 0, limits.maxJsonValueDepth, "mutation"));
        if (kind.equals("and")) return new SemanticAndFilter(mapExpressions(filter.expressions(), depth, count, limits));
        if (kind.equals("or")) return new SemanticOrFilter(mapExpressions(filter.expressions(), depth, count, limits));
        throw new IllegalArgumentException("Unsupported mutation filter. Supported kinds are field, and and or.");
    }

    private static List<SemanticFilterExpression> mapExpressions(List<FoundgineGatewayMutationRequest.Filter> filters, int depth,
            Counter count, FoundgineGatewayHttpOptions limits) {
        if (filters == null || filters.isEmpty()) throw new IllegalArgumentException("AND/OR filters require a non-empty expressions array.");
        var mapped = new ArrayList<SemanticFilterExpression>(filters.size());
        for (var f : filters) mapped.add(mapFilter(f, depth + 1, count, limits));
        return mapped;
    }
}
