package com.foundgine.extensions.graphql;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.SemanticEntity;
import com.foundgine.core.semantic.SemanticField;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticRelationship;
import com.foundgine.core.semantic.SemanticRequest;
import com.foundgine.core.semantic.SemanticSelection;
import com.foundgine.core.semantic.query.SemanticAggregateFilter;
import com.foundgine.core.semantic.query.SemanticAggregateFilterOperator;
import com.foundgine.core.semantic.query.SemanticAndFilter;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticOrFilter;
import com.foundgine.core.semantic.query.SemanticOrderAggregate;
import com.foundgine.core.semantic.query.SemanticOrderTerm;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.core.semantic.query.SemanticSortDirection;
import com.foundgine.extensions.graphql.GraphQLResultShape.GraphQLResultField;
import graphql.language.Argument;
import graphql.language.Definition;
import graphql.language.Document;
import graphql.language.Field;
import graphql.language.FragmentDefinition;
import graphql.language.FragmentSpread;
import graphql.language.InlineFragment;
import graphql.language.OperationDefinition;
import graphql.language.Selection;
import graphql.language.SelectionSet;
import graphql.language.VariableDefinition;
import graphql.parser.Parser;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Objects;
import java.util.Set;

/**
 * Translates GraphQL query syntax into provider-neutral {@link SemanticRequest} objects. GraphQL aliases, fragments,
 * directives and variables are consumed here and never leak into Foundgine core.
 *
 * <p>Port of the C# {@code HotChocolateSemanticAdapter}. The C# adapter parses with {@code HotChocolate.Language}; this
 * one parses with graphql-java ({@code graphql.parser.Parser}, {@code graphql.language.*}), the de-facto GraphQL engine
 * on the JVM. Runtime variables are plain Java values (see {@link GraphQLVariableCoercer}).
 */
public final class GraphQLSemanticAdapter {
    private final SemanticModel model;

    public GraphQLSemanticAdapter(SemanticModel model) {
        this.model = Objects.requireNonNull(model, "model");
    }

    public SemanticRequest adapt(String graphql) {
        return adaptResultShape(graphql).request();
    }

    public SemanticRequest adapt(String graphql, Map<String, Object> variables, String operationName) {
        return adaptResultShape(graphql, variables, operationName).request();
    }

    public SemanticRequest adapt(String graphql, String operationName) {
        return adaptResultShape(graphql, null, operationName).request();
    }

    public GraphQLQueryAdaptation adaptResultShape(String graphql) {
        return adaptResultShape(graphql, null, null);
    }

    public GraphQLQueryAdaptation adaptResultShape(String graphql, Map<String, Object> variables, String operationName) {
        if (graphql == null || graphql.isBlank())
            throw new IllegalArgumentException("graphql must not be null or blank.");

        Document document = Parser.parse(graphql);
        OperationDefinition operation = selectOperation(document, OperationDefinition.Operation.QUERY, operationName);
        Map<String, VariableDefinition> variableDefinitions = variableDefinitions(operation);

        GraphQLVariableCoercer.validateSuppliedVariables(variables, variableDefinitions);

        if (!operation.getDirectives().isEmpty())
            throw new IllegalStateException("GraphQL query operation directives are not supported by the adapter.");

        List<Field> roots = rootFields(operation.getSelectionSet());
        if (roots.size() != 1)
            throw new IllegalStateException("Foundgine SemanticRequest supports exactly one root GraphQL field.");

        Field rootField = roots.get(0);
        SemanticEntity rootEntity = findEntity(rootField.getName());
        if (rootField.getSelectionSet() == null)
            throw new IllegalStateException("Root GraphQL field '" + rootField.getName() + "' must select fields.");

        Map<String, FragmentDefinition> fragments = fragments(document);
        SemanticQueryOptions options = translateRootArguments(rootField, rootEntity, variables, variableDefinitions);
        Selected selection = translateSelectionSet(
                rootEntity, rootField.getSelectionSet(), fragments, variables, variableDefinitions, new HashSet<>());

        var request = new SemanticRequest(rootEntity.id(), selection.request(), options, null);
        return new GraphQLQueryAdaptation(request, selection.result());
    }

    /** Non-throwing variant: adapter failures are returned as structured {@link GraphQLAdapterError}s. */
    public GraphQLAdapterResult<GraphQLQueryAdaptation> tryAdapt(
            String graphql, Map<String, Object> variables, String operationName) {
        try {
            return GraphQLAdapterResult.success(adaptResultShape(graphql, variables, operationName));
        } catch (Exception exception) {
            return GraphQLAdapterResult.failure(GraphQLAdapterErrors.fromException(exception));
        }
    }

    public GraphQLAdapterResult<GraphQLQueryAdaptation> tryAdapt(String graphql) {
        return tryAdapt(graphql, null, null);
    }

    /** Adapts an already-parsed root selection set (used by executors that parse once). */
    public SemanticRequest adapt(SelectionSet selectionSet) {
        Objects.requireNonNull(selectionSet, "selectionSet");

        List<Field> roots = rootFields(selectionSet);
        if (roots.size() != 1)
            throw new IllegalStateException("Foundgine SemanticRequest supports exactly one root GraphQL field.");

        Field rootField = roots.get(0);
        SemanticEntity rootEntity = findEntity(rootField.getName());
        if (rootField.getSelectionSet() == null)
            throw new IllegalStateException("Root GraphQL field '" + rootField.getName() + "' must select fields.");

        Selected selection = translateSelectionSet(
                rootEntity, rootField.getSelectionSet(), Map.of(), null, Map.of(), new HashSet<>());

        return new SemanticRequest(
                rootEntity.id(),
                selection.request(),
                translateRootArguments(rootField, rootEntity, null, Map.of()),
                null);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Document helpers
    // ---------------------------------------------------------------------------------------------------------------

    static Map<String, VariableDefinition> variableDefinitions(OperationDefinition operation) {
        var result = new LinkedHashMap<String, VariableDefinition>();
        for (VariableDefinition definition : operation.getVariableDefinitions())
            if (result.put(definition.getName(), definition) != null)
                throw new IllegalArgumentException(
                        "An item with the same key has already been added. Key: " + definition.getName());
        return result;
    }

    static Map<String, FragmentDefinition> fragments(Document document) {
        var result = new LinkedHashMap<String, FragmentDefinition>();
        for (Definition<?> definition : document.getDefinitions())
            if (definition instanceof FragmentDefinition fragment)
                if (result.put(fragment.getName(), fragment) != null)
                    throw new IllegalArgumentException(
                            "An item with the same key has already been added. Key: " + fragment.getName());
        return result;
    }

    static OperationDefinition selectOperation(
            Document document, OperationDefinition.Operation expectedType, String operationName) {
        var operations = new ArrayList<OperationDefinition>();
        for (Definition<?> definition : document.getDefinitions())
            if (definition instanceof OperationDefinition op) operations.add(op);

        if (operations.isEmpty())
            throw new IllegalStateException("GraphQL document contains no operation definitions.");

        OperationDefinition operation = null;
        if (operationName != null) {
            for (var candidate : operations)
                if (operationName.equals(candidate.getName())) {
                    operation = candidate;
                    break;
                }
            if (operation == null)
                throw new IllegalStateException("GraphQL operation '" + operationName + "' was not found.");
        } else {
            if (operations.size() > 1)
                throw new IllegalStateException(
                        "GraphQL document contains multiple operations; an operation name is required.");
            operation = operations.get(0);
        }

        if (operation.getOperation() != expectedType)
            throw new IllegalStateException(expectedType == OperationDefinition.Operation.QUERY
                    ? "Selected GraphQL operation supports query operations only."
                    : "Selected GraphQL operation supports mutation operations only.");

        return operation;
    }

    private static List<Field> rootFields(SelectionSet selectionSet) {
        var roots = new ArrayList<Field>();
        for (Selection<?> selection : selectionSet.getSelections())
            if (selection instanceof Field field && !isMetaField(field.getName())) roots.add(field);
        return roots;
    }

    private SemanticEntity findEntity(String name) {
        for (SemanticEntity entity : model.entities())
            if (namesEqual(entity.name(), name)) return entity;
        throw new IllegalStateException("GraphQL entity '" + name + "' is not defined in the semantic model.");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Selection translation
    // ---------------------------------------------------------------------------------------------------------------

    private record Selected(List<SemanticSelection> request, GraphQLResultShape result) { }

    private record TranslatedField(SemanticSelection request, GraphQLResultField result) { }

    private Selected translateSelectionSet(
            SemanticEntity entity,
            SelectionSet selectionSet,
            Map<String, FragmentDefinition> fragments,
            Map<String, Object> variables,
            Map<String, VariableDefinition> variableDefinitions,
            Set<String> fragmentStack) {
        var request = new ArrayList<SemanticSelection>();
        var resultFields = new ArrayList<GraphQLResultField>();
        var responseNames = new HashSet<String>();

        for (Selection<?> selection : selectionSet.getSelections()) {
            if (selection instanceof Field field) {
                if (!GraphQLDirectiveEvaluator.shouldInclude(field.getDirectives(), variables, variableDefinitions))
                    continue;
                if (isMetaField(field.getName())) continue;

                validateFieldShape(field);
                TranslatedField translated =
                        translateField(entity, field, fragments, variables, variableDefinitions, fragmentStack);
                request.add(translated.request());
                addResultField(responseNames, resultFields, translated.result());
            } else if (selection instanceof InlineFragment inline) {
                if (!GraphQLDirectiveEvaluator.shouldInclude(inline.getDirectives(), variables, variableDefinitions))
                    continue;

                validateFragmentType(
                        entity,
                        inline.getTypeCondition() == null ? null : inline.getTypeCondition().getName(),
                        "inline fragment");

                if (inline.getSelectionSet() == null) continue;

                Selected inlineResult = translateSelectionSet(
                        entity, inline.getSelectionSet(), fragments, variables, variableDefinitions, fragmentStack);
                request.addAll(inlineResult.request());
                for (var result : inlineResult.result().fields()) addResultField(responseNames, resultFields, result);
            } else if (selection instanceof FragmentSpread spread) {
                if (!GraphQLDirectiveEvaluator.shouldInclude(spread.getDirectives(), variables, variableDefinitions))
                    continue;

                FragmentDefinition fragment = fragments.get(spread.getName());
                if (fragment == null)
                    throw new IllegalStateException("GraphQL fragment '" + spread.getName() + "' was not found.");

                validateFragmentType(
                        entity, fragment.getTypeCondition().getName(), "fragment '" + spread.getName() + "'");

                if (!fragmentStack.add(spread.getName()))
                    throw new IllegalStateException("GraphQL fragment cycle detected at '" + spread.getName() + "'.");

                Selected fragmentResult = translateSelectionSet(
                        entity, fragment.getSelectionSet(), fragments, variables, variableDefinitions, fragmentStack);

                fragmentStack.remove(spread.getName());

                request.addAll(fragmentResult.request());
                for (var result : fragmentResult.result().fields())
                    addResultField(responseNames, resultFields, result);
            } else {
                throw new IllegalStateException(
                        "GraphQL selection '" + selection.getClass().getSimpleName() + "' is not supported.");
            }
        }

        return new Selected(request, new GraphQLResultShape(resultFields));
    }

    private TranslatedField translateField(
            SemanticEntity entity,
            Field field,
            Map<String, FragmentDefinition> fragments,
            Map<String, Object> variables,
            Map<String, VariableDefinition> variableDefinitions,
            Set<String> fragmentStack) {
        SemanticField semanticField = findField(entity, field.getName());
        String alias = field.getAlias() != null ? field.getAlias() : field.getName();

        if (semanticField != null) {
            if (field.getSelectionSet() != null)
                throw new IllegalStateException(
                        "Scalar GraphQL field '" + field.getName() + "' cannot contain a selection set.");

            return new TranslatedField(
                    new SemanticSelection(semanticField.id(), null, List.of()),
                    new GraphQLResultField(semanticField.id(), null, field.getName(), alias));
        }

        SemanticRelationship relationship = findRelationship(entity, field.getName());
        if (relationship == null)
            throw new IllegalStateException("GraphQL field '" + field.getName()
                    + "' is not defined on semantic entity '" + entity.name() + "'.");

        if (field.getSelectionSet() == null)
            throw new IllegalStateException(
                    "Relationship GraphQL field '" + field.getName() + "' must contain a selection set.");

        SemanticEntity target = model.get(relationship.target());
        Selected children = translateSelectionSet(
                target, field.getSelectionSet(), fragments, variables, variableDefinitions, fragmentStack);

        return new TranslatedField(
                new SemanticSelection(null, relationship.id(), children.request()),
                new GraphQLResultField(null, relationship.id(), field.getName(), alias, children.result()));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Root arguments: where / order / first / skip / offset / after
    // ---------------------------------------------------------------------------------------------------------------

    private SemanticQueryOptions translateRootArguments(
            Field rootField,
            SemanticEntity entity,
            Map<String, Object> variables,
            Map<String, VariableDefinition> variableDefinitions) {
        SemanticFilterExpression filter = null;
        var order = new ArrayList<SemanticOrderTerm>();
        Integer limit = null;
        Integer offset = null;
        String after = null;

        for (Argument argument : rootField.getArguments()) {
            Object value = GraphQLVariableCoercer.resolveValue(argument.getValue(), variables, variableDefinitions);
            String name = argument.getName();

            switch (name) {
                case "where" -> {
                    if (filter != null)
                        throw new IllegalStateException("GraphQL root field may specify 'where' only once.");
                    filter = translateFilterValue(value, entity);
                }
                case "order" -> order.addAll(translateOrderValue(value, entity, List.of()));
                case "first" -> {
                    limit = readInt(value, "first");
                    if (limit < 0) throw new IllegalStateException("'first' must be non-negative.");
                }
                case "skip", "offset" -> {
                    offset = readInt(value, name);
                    if (offset < 0) throw new IllegalStateException("'" + name + "' must be non-negative.");
                }
                case "after" -> {
                    if (!(value instanceof String cursor))
                        throw new IllegalStateException("GraphQL argument 'after' must be a string cursor.");
                    after = cursor;
                }
                case "last", "before" -> throw new IllegalStateException("GraphQL argument '" + name
                        + "' is not supported by the forward keyset pagination contract.");
                default -> throw new IllegalStateException(
                        "GraphQL argument '" + name + "' is not supported by the semantic query contract.");
            }
        }

        return new SemanticQueryOptions(filter, order, limit, offset, after);
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> asObject(Object value) {
        return value instanceof Map<?, ?> map ? (Map<String, Object>) map : null;
    }

    /** A GraphQL list value (never a string), or null when {@code value} is not list-like. */
    private static List<Object> asList(Object value) {
        if (value == null || value instanceof String) return null;
        var result = new ArrayList<Object>();
        if (value instanceof Iterable<?> iterable) {
            for (var item : iterable) result.add(item);
            return result;
        }
        if (value.getClass().isArray()) {
            int length = java.lang.reflect.Array.getLength(value);
            for (int i = 0; i < length; i++) result.add(java.lang.reflect.Array.get(value, i));
            return result;
        }
        return null;
    }

    private SemanticFilterExpression translateFilterValue(Object value, SemanticEntity entity) {
        Map<String, Object> dictionary = asObject(value);
        if (dictionary == null) throw new IllegalStateException("GraphQL 'where' must be an input object.");

        var expressions = new ArrayList<SemanticFilterExpression>();
        for (var pair : dictionary.entrySet()) {
            String key = pair.getKey();
            Object pairValue = pair.getValue();

            if (key.equalsIgnoreCase("and") || key.equalsIgnoreCase("or")) {
                String label = key.toLowerCase(Locale.ROOT);
                List<Object> list = asList(pairValue);
                if (list == null) throw new IllegalStateException("'" + label + "' must be a list of filter objects.");

                var nested = new ArrayList<SemanticFilterExpression>();
                for (var item : list) nested.add(translateFilterValue(item, entity));
                expressions.add(label.equals("and") ? new SemanticAndFilter(nested) : new SemanticOrFilter(nested));
                continue;
            }

            SemanticRelationship relationship = findRelationship(entity, key);
            if (relationship != null) {
                expressions.addAll(translateRelationshipFilterValue(pairValue, relationship));
                continue;
            }

            SemanticField semanticField = findField(entity, key);
            if (semanticField == null)
                throw new IllegalStateException("Filter field '" + key + "' is not defined on '" + entity.name() + "'.");

            Map<String, Object> operators = asObject(pairValue);
            if (operators != null) {
                for (var op : operators.entrySet()) {
                    List<Object> list = asList(op.getValue());
                    Object operand = list != null && op.getKey().equalsIgnoreCase("in") ? list : op.getValue();
                    expressions.add(new SemanticFieldFilter(semanticField.id(), parseFilterOperator(op.getKey()), operand));
                }
            } else {
                expressions.add(new SemanticFieldFilter(semanticField.id(), SemanticFilterOperator.EQ, pairValue));
            }
        }

        if (expressions.isEmpty()) throw new IllegalStateException("GraphQL 'where' cannot be empty.");

        return expressions.size() == 1 ? expressions.get(0) : new SemanticAndFilter(expressions);
    }

    private List<SemanticFilterExpression> translateRelationshipFilterValue(
            Object value, SemanticRelationship relationship) {
        if (relationship.cardinality() != RelationshipCardinality.MANY)
            throw new IllegalStateException(
                    "Relationship filter '" + relationship.name() + "' requires a collection relationship.");

        Map<String, Object> dictionary = asObject(value);
        if (dictionary == null)
            throw new IllegalStateException("Relationship filter '" + relationship.name() + "' must be an input object.");

        SemanticEntity target = model.get(relationship.target());
        var expressions = new ArrayList<SemanticFilterExpression>();

        for (var aggregateField : dictionary.entrySet()) {
            String key = aggregateField.getKey();
            if (key.equalsIgnoreCase("count") || key.equalsIgnoreCase("_count")) {
                expressions.addAll(parseAggregateObject(
                        aggregateField.getValue(), relationship.id(), SemanticFilterAggregate.COUNT, null));
                continue;
            }

            Map<String, Object> fieldAggregate = asObject(aggregateField.getValue());
            if (fieldAggregate == null)
                throw new IllegalStateException("Collection filter field '" + key + "' must specify min or max.");

            SemanticField targetField = findField(target, key);
            if (targetField == null)
                throw new IllegalStateException(
                        "Aggregate filter field '" + key + "' is not defined on '" + target.name() + "'.");

            for (var aggregate : fieldAggregate.entrySet()) {
                SemanticFilterAggregate kind =
                        switch (aggregate.getKey().toLowerCase(Locale.ROOT)) {
                            case "min" -> SemanticFilterAggregate.MIN;
                            case "max" -> SemanticFilterAggregate.MAX;
                            default -> throw new IllegalStateException(
                                    "Aggregate filter '" + aggregate.getKey() + "' is not supported.");
                        };

                Map<String, Object> operators = asObject(aggregate.getValue());
                if (operators == null)
                    throw new IllegalStateException("Aggregate filter '" + aggregate.getKey() + "' for '"
                            + targetField.name() + "' must be an input object.");

                for (var op : operators.entrySet())
                    expressions.add(new SemanticAggregateFilter(
                            relationship.id(), kind, targetField.id(), parseAggregateOperator(op.getKey()), op.getValue()));
            }
        }

        return expressions;
    }

    private static List<SemanticAggregateFilter> parseAggregateObject(
            Object value, RelationshipId relationship, SemanticFilterAggregate aggregate, FieldId field) {
        Map<String, Object> dictionary = asObject(value);
        if (dictionary == null) throw new IllegalStateException("Aggregate filter must be an input object.");

        var result = new ArrayList<SemanticAggregateFilter>();
        for (var op : dictionary.entrySet())
            result.add(new SemanticAggregateFilter(
                    relationship, aggregate, field, parseAggregateOperator(op.getKey()), op.getValue()));
        return result;
    }

    private List<SemanticOrderTerm> translateOrderValue(
            Object value, SemanticEntity entity, List<RelationshipId> path) {
        Map<String, Object> dictionary = asObject(value);
        if (dictionary == null) throw new IllegalStateException("GraphQL 'order' must be an input object.");

        var terms = new ArrayList<SemanticOrderTerm>();

        for (var pair : dictionary.entrySet()) {
            String key = pair.getKey();
            Object pairValue = pair.getValue();

            SemanticField semanticField = findField(entity, key);
            if (semanticField != null) {
                if (pairValue instanceof String direction) {
                    terms.add(new SemanticOrderTerm(
                            semanticField.id(), parseSortDirection(direction), path, SemanticOrderAggregate.NONE));
                    continue;
                }

                Map<String, Object> aggregateObject = asObject(pairValue);
                if (aggregateObject != null && !path.isEmpty()) {
                    for (var aggregate : aggregateObject.entrySet()) {
                        SemanticOrderAggregate kind = parseOrderAggregate(aggregate.getKey());
                        if (!(aggregate.getValue() instanceof String aggregateDirection))
                            throw new IllegalStateException(
                                    "Order aggregate '" + aggregate.getKey() + "' must use ASC or DESC.");
                        terms.add(new SemanticOrderTerm(
                                semanticField.id(), parseSortDirection(aggregateDirection), path, kind));
                    }
                    continue;
                }

                throw new IllegalStateException(
                        "Order field '" + key + "' must use ASC/DESC or an aggregate object.");
            }

            SemanticRelationship relationship = findRelationship(entity, key);
            if (relationship == null)
                throw new IllegalStateException("Order field '" + key + "' is not defined on '" + entity.name() + "'.");

            Map<String, Object> nested = asObject(pairValue);
            if (nested == null)
                throw new IllegalStateException("Order relationship '" + key + "' must contain nested order fields.");

            var nextPath = new ArrayList<RelationshipId>(path);
            nextPath.add(relationship.id());
            SemanticEntity target = model.get(relationship.target());

            if (relationship.cardinality() == RelationshipCardinality.MANY) {
                for (var aggregateField : nested.entrySet()) {
                    String aggregateKey = aggregateField.getKey();
                    if (aggregateKey.equalsIgnoreCase("_count")) {
                        if (!(aggregateField.getValue() instanceof String countDirection))
                            throw new IllegalStateException("Collection _count order must use ASC or DESC.");
                        terms.add(new SemanticOrderTerm(
                                target.identity().fieldId(),
                                parseSortDirection(countDirection),
                                nextPath,
                                SemanticOrderAggregate.COUNT));
                        continue;
                    }

                    SemanticField targetField = findField(target, aggregateKey);
                    if (targetField == null)
                        throw new IllegalStateException("Aggregate order field '" + aggregateKey
                                + "' is not defined on '" + target.name() + "'.");

                    Map<String, Object> aggregateObject = asObject(aggregateField.getValue());
                    if (aggregateObject == null)
                        throw new UnsupportedOperationException("Collection relationship order field '"
                                + aggregateKey + "' must specify min or max.");

                    for (var aggregate : aggregateObject.entrySet()) {
                        SemanticOrderAggregate kind = parseOrderAggregate(aggregate.getKey());
                        if (!(aggregate.getValue() instanceof String direction))
                            throw new IllegalStateException(
                                    "Order aggregate '" + aggregate.getKey() + "' must use ASC or DESC.");
                        terms.add(new SemanticOrderTerm(
                                targetField.id(), parseSortDirection(direction), nextPath, kind));
                    }
                }
                continue;
            }

            terms.addAll(translateOrderValue(nested, target, nextPath));
        }

        return terms;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Name / value helpers
    // ---------------------------------------------------------------------------------------------------------------

    private static SemanticRelationship findRelationship(SemanticEntity entity, String name) {
        for (SemanticRelationship relationship : entity.relationships())
            if (namesEqual(relationship.name(), name)) return relationship;
        return null;
    }

    static SemanticField findField(SemanticEntity entity, String name) {
        for (SemanticField field : entity.fields())
            if (namesEqual(field.name(), name)) return field;

        if (namesEqual(entity.identity().name(), name) || name.equalsIgnoreCase("id"))
            return new SemanticField(entity.identity().fieldId(), entity.identity().name(), Object.class);

        return null;
    }

    private static SemanticFilterOperator parseFilterOperator(String value) {
        return switch (value.toLowerCase(Locale.ROOT)) {
            case "eq" -> SemanticFilterOperator.EQ;
            case "neq" -> SemanticFilterOperator.NEQ;
            case "in" -> SemanticFilterOperator.IN;
            default -> throw new IllegalStateException("Filter operator '" + value + "' is not supported.");
        };
    }

    private static SemanticAggregateFilterOperator parseAggregateOperator(String value) {
        return switch (value.toLowerCase(Locale.ROOT)) {
            case "eq" -> SemanticAggregateFilterOperator.EQ;
            case "neq" -> SemanticAggregateFilterOperator.NEQ;
            case "gt" -> SemanticAggregateFilterOperator.GT;
            case "gte" -> SemanticAggregateFilterOperator.GTE;
            case "lt" -> SemanticAggregateFilterOperator.LT;
            case "lte" -> SemanticAggregateFilterOperator.LTE;
            default -> throw new IllegalStateException("Aggregate filter operator '" + value + "' is not supported.");
        };
    }

    private static SemanticOrderAggregate parseOrderAggregate(String value) {
        return switch (value.toLowerCase(Locale.ROOT)) {
            case "min" -> SemanticOrderAggregate.MIN;
            case "max" -> SemanticOrderAggregate.MAX;
            default -> throw new IllegalStateException("Order aggregate '" + value + "' is not supported.");
        };
    }

    private static SemanticSortDirection parseSortDirection(String value) {
        if (value.equalsIgnoreCase("DESC")) return SemanticSortDirection.DESC;
        if (value.equalsIgnoreCase("ASC")) return SemanticSortDirection.ASC;
        throw new IllegalStateException("Order direction '" + value + "' is not supported.");
    }

    private static int readInt(Object value, String name) {
        if (value instanceof Byte || value instanceof Short || value instanceof Integer)
            return ((Number) value).intValue();
        if (value instanceof Long l && l >= Integer.MIN_VALUE && l <= Integer.MAX_VALUE) return l.intValue();
        throw new IllegalStateException("GraphQL argument '" + name + "' must be an integer.");
    }

    private static void validateFieldShape(Field field) {
        if (!field.getArguments().isEmpty())
            throw new IllegalStateException("GraphQL arguments on field '" + field.getName()
                    + "' are not supported by the semantic adapter.");
    }

    private static void validateFragmentType(SemanticEntity entity, String typeCondition, String context) {
        if (typeCondition == null) return;
        if (!entity.name().equalsIgnoreCase(typeCondition))
            throw new IllegalStateException("GraphQL " + context + " targets '" + typeCondition
                    + "' and cannot be applied to selection type '" + entity.name() + "'.");
    }

    private static void addResultField(
            Set<String> responseNames, List<GraphQLResultField> fields, GraphQLResultField field) {
        if (!responseNames.add(field.alias())) {
            for (var existing : fields)
                if (existing.alias().equals(field.alias())
                        && Objects.equals(existing.field(), field.field())
                        && Objects.equals(existing.relationship(), field.relationship()))
                    return;

            throw new IllegalStateException(
                    "GraphQL result contains duplicate response alias/name '" + field.alias() + "'.");
        }
        fields.add(field);
    }

    private static boolean isMetaField(String name) {
        return name.startsWith("__");
    }

    static boolean namesEqual(String schemaName, String graphqlName) {
        return schemaName.equalsIgnoreCase(graphqlName) || toGraphQLName(schemaName).equals(graphqlName);
    }

    static String toGraphQLName(String name) {
        return name == null || name.isEmpty() ? name : Character.toLowerCase(name.charAt(0)) + name.substring(1);
    }
}
