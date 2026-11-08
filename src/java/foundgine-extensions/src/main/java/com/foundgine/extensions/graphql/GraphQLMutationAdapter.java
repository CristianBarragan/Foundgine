package com.foundgine.extensions.graphql;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.SemanticEntity;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticRelationship;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.IMetadataProvider;
import com.foundgine.core.semantic.planning.mutation.IMutationIntent;
import com.foundgine.core.semantic.planning.mutation.MutationFieldValue;
import com.foundgine.core.semantic.planning.mutation.MutationIntent;
import com.foundgine.core.semantic.planning.mutation.MutationKind;
import com.foundgine.core.semantic.planning.mutation.NestedMutationChild;
import com.foundgine.core.semantic.planning.mutation.NestedMutationIntent;
import com.foundgine.core.semantic.planning.mutation.UpsertIntent;
import com.foundgine.core.semantic.query.SemanticAndFilter;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.extensions.graphql.GraphQLMutationResultShape.GraphQLMutationResultField;
import com.foundgine.extensions.graphql.GraphQLMutationResultShape.GraphQLMutationResultRelationship;
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
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Objects;
import java.util.Set;

/**
 * Translates GraphQL mutation syntax into provider-neutral Foundgine mutation intents. GraphQL variables, fragments,
 * aliases, directives and output projection remain entirely at this adapter boundary.
 *
 * <p>Port of the C# {@code HotChocolateMutationAdapter}; parsing is done with graphql-java's {@link Parser} instead of
 * {@code HotChocolate.Language}. Java has no optional parameters, so {@code variables} / {@code operationName} are
 * explicit overloads; passing a bare {@code null} for both of the last two arguments of {@code adapt(String, ?, ?)} is
 * ambiguous and needs a cast.
 *
 * <p>Value coercion ({@link GraphQLMutationValueCoercer}) mirrors {@code CoerceMutationValue}.
 */
public final class GraphQLMutationAdapter {
    private final SemanticModel model;
    private final IMetadataProvider metadata;

    public GraphQLMutationAdapter(SemanticModel model, IMetadataProvider metadata) {
        this.model = Objects.requireNonNull(model, "model");
        this.metadata = Objects.requireNonNull(metadata, "metadata");
    }

    public SemanticModel model() {
        return model;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Entry points
    // ---------------------------------------------------------------------------------------------------------------

    public NestedMutationIntent adapt(String graphql) {
        return adaptResultShape(graphql, null, null).intent();
    }

    public NestedMutationIntent adapt(String graphql, String operationName, Map<String, Object> variables) {
        return adaptResultShape(graphql, variables, operationName).intent();
    }

    /**
     * Operation-name-only form. C# reaches this through {@code Adapt(graphql, operationName, variables = null)}; Java has
     * no optional parameters, so the overload is explicit.
     */
    public NestedMutationIntent adapt(String graphql, String operationName) {
        return adapt(graphql, operationName, null);
    }

    public NestedMutationIntent adapt(String graphql, Map<String, Object> variables) {
        return adapt(graphql, null, variables);
    }

    public NestedMutationIntent adapt(String graphql, Map<String, Object> variables, String operationName) {
        return adapt(graphql, operationName, variables);
    }

    public GraphQLMutationAdaptation adaptWithResultShape(String graphql) {
        return adaptWithResultShape(graphql, null, null);
    }

    public GraphQLMutationAdaptation adaptWithResultShape(
            String graphql, Map<String, Object> variables, String operationName) {
        requireText(graphql);

        var document = Parser.parse(graphql);
        var operation = GraphQLSemanticAdapter.selectOperation(
                document, OperationDefinition.Operation.MUTATION, operationName);
        var variableDefinitions = GraphQLSemanticAdapter.variableDefinitions(operation);
        GraphQLVariableCoercer.validateSuppliedVariables(variables, variableDefinitions);

        if (!operation.getDirectives().isEmpty())
            throw new IllegalStateException("GraphQL mutation operation directives are not supported by the adapter.");

        var fields = rootFields(operation.getSelectionSet());
        if (fields.size() != 1)
            throw new IllegalStateException("Foundgine GraphQL mutation adapter supports exactly one root mutation field.");

        var root = fields.get(0);
        if (root.getAlias() != null)
            throw new IllegalStateException("GraphQL mutation root aliases are not supported by the mutation contract.");

        return adaptRootField(root, document, variableDefinitions, variables);
    }

    public GraphQLMutationAdaptation adaptResultShape(
            String graphql, Map<String, Object> variables, String operationName) {
        return adaptWithResultShape(graphql, variables, operationName);
    }

    /**
     * Batch form of {@code adaptWithResultShape}: accepts a mutation document with MORE THAN ONE root field, each of
     * which becomes an independent mutation (createX, updateX, deleteX, upsertX; each may still have its own nested
     * children). Every root field in a batch document MUST be aliased; that alias is the key used to correlate each
     * result back to its request ({@link GraphQLMutationBatchItem#resultKey()}).
     *
     * <p>A document with exactly one, unaliased root field is not a batch: it is routed through the single-field contract
     * and keyed by its field name. Combine the returned items' {@code intent()} values with the planner's batch overload
     * to get one dependency-aware plan for the whole document.
     */
    public List<GraphQLMutationBatchItem> adaptBatchWithResultShape(String graphql) {
        return adaptBatchWithResultShape(graphql, null, null);
    }

    public List<GraphQLMutationBatchItem> adaptBatchWithResultShape(
            String graphql, Map<String, Object> variables, String operationName) {
        requireText(graphql);

        var document = Parser.parse(graphql);
        var operation = GraphQLSemanticAdapter.selectOperation(
                document, OperationDefinition.Operation.MUTATION, operationName);
        var variableDefinitions = GraphQLSemanticAdapter.variableDefinitions(operation);
        GraphQLVariableCoercer.validateSuppliedVariables(variables, variableDefinitions);

        if (!operation.getDirectives().isEmpty())
            throw new IllegalStateException("GraphQL mutation operation directives are not supported by the adapter.");

        var fields = rootFields(operation.getSelectionSet());
        if (fields.isEmpty()) throw new IllegalStateException("GraphQL mutation operation contains no root fields.");

        if (fields.size() == 1 && fields.get(0).getAlias() == null) {
            return List.of(new GraphQLMutationBatchItem(
                    fields.get(0).getName(), adaptRootField(fields.get(0), document, variableDefinitions, variables)));
        }

        var seenKeys = new HashSet<String>();
        var items = new ArrayList<GraphQLMutationBatchItem>(fields.size());
        for (var field : fields) {
            if (field.getAlias() == null)
                throw new IllegalStateException("Root mutation field '" + field.getName()
                        + "' must be aliased when a mutation document contains more than one root field, "
                        + "so each result can be correlated back to its request.");

            var key = field.getAlias();
            if (!seenKeys.add(key))
                throw new IllegalStateException("Duplicate root mutation alias '" + key + "' in batch document.");

            items.add(new GraphQLMutationBatchItem(key, adaptRootField(field, document, variableDefinitions, variables)));
        }
        return List.copyOf(items);
    }

    public GraphQLAdapterResult<List<GraphQLMutationBatchItem>> tryAdaptBatch(
            String graphql, Map<String, Object> variables, String operationName) {
        try {
            return GraphQLAdapterResult.success(adaptBatchWithResultShape(graphql, variables, operationName));
        } catch (Exception exception) {
            return GraphQLAdapterResult.failure(GraphQLAdapterErrors.fromException(exception));
        }
    }

    public GraphQLAdapterResult<NestedMutationIntent> tryAdapt(String graphql) {
        return tryAdapt(graphql, null, (String) null);
    }

    public GraphQLAdapterResult<NestedMutationIntent> tryAdapt(
            String graphql, Map<String, Object> variables, String operationName) {
        try {
            return GraphQLAdapterResult.success(adapt(graphql, operationName, variables));
        } catch (Exception exception) {
            return GraphQLAdapterResult.failure(GraphQLAdapterErrors.fromException(exception));
        }
    }

    public GraphQLAdapterResult<NestedMutationIntent> tryAdapt(
            String graphql, String operationName, Map<String, Object> variables) {
        return tryAdapt(graphql, variables, operationName);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Root field
    // ---------------------------------------------------------------------------------------------------------------

    private static void requireText(String graphql) {
        if (graphql == null || graphql.isBlank())
            throw new IllegalArgumentException("graphql must not be null or blank.");
    }

    private static List<Field> rootFields(SelectionSet selectionSet) {
        var roots = new ArrayList<Field>();
        for (Selection<?> selection : selectionSet.getSelections())
            if (selection instanceof Field field && !field.getName().startsWith("__")) roots.add(field);
        return roots;
    }

    private GraphQLMutationAdaptation adaptRootField(
            Field root,
            Document document,
            Map<String, VariableDefinition> variableDefinitions,
            Map<String, Object> variables) {
        if (!root.getDirectives().isEmpty())
            throw new IllegalStateException(
                    "GraphQL mutation directives on the root mutation field are not supported by the adapter.");

        var parsed = parseOperation(root.getName());
        var entity = findEntity(parsed.entityName());
        var input = readInput(root, parsed.kind(), variables, variableDefinitions);

        var rootMutation = buildMutation(entity, parsed.kind(), input);
        var children = new ArrayList<NestedMutationChild>();
        for (var child : input.nestedChildren())
            children.add(new NestedMutationChild(child.relationship().id(), child.intent()));
        var nestedIntent = new NestedMutationIntent(rootMutation, children);

        var resultShape = translateReturnShape(
                entity,
                root.getSelectionSet(),
                GraphQLSemanticAdapter.fragments(document),
                variables,
                variableDefinitions,
                new HashSet<>());

        nestedIntent = applyReturnShape(nestedIntent, resultShape);
        return new GraphQLMutationAdaptation(nestedIntent, resultShape);
    }

    private record ParsedOperation(MutationKind kind, String entityName) {}

    private static ParsedOperation parseOperation(String name) {
        for (var prefix : new String[] {"create", "update", "delete", "upsert"}) {
            if (name.regionMatches(true, 0, prefix, 0, prefix.length()) && name.length() > prefix.length()) {
                var kind = switch (prefix) {
                    case "create" -> MutationKind.CREATE;
                    case "update" -> MutationKind.UPDATE;
                    case "delete" -> MutationKind.DELETE;
                    default -> MutationKind.UPSERT;
                };
                return new ParsedOperation(kind, name.substring(prefix.length()));
            }
        }
        throw new IllegalStateException(
                "GraphQL mutation field '" + name + "' must use createX, updateX, deleteX, or upsertX naming.");
    }

    private SemanticEntity findEntity(String name) {
        for (var entity : model.entities()) if (entity.name().equalsIgnoreCase(name)) return entity;
        throw new IllegalStateException("GraphQL mutation entity '" + name + "' is not defined in the semantic model.");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Input
    // ---------------------------------------------------------------------------------------------------------------

    private record NestedChildInput(SemanticRelationship relationship, NestedMutationIntent intent) {}

    private record MutationBuildResult(
            List<MutationFieldValue> fields,
            SemanticFilterExpression filter,
            List<ColumnId> conflictColumns,
            List<NestedChildInput> nestedChildren) {}

    private MutationBuildResult readInput(
            Field field,
            MutationKind kind,
            Map<String, Object> variables,
            Map<String, VariableDefinition> variableDefinitions) {
        var entity = findEntity(parseOperation(field.getName()).entityName());
        Object input = null;
        SemanticFilterExpression filter = null;
        List<ColumnId> conflictColumns = null;

        for (var argument : field.getArguments()) {
            switch (argument.getName()) {
                case "input" -> input = GraphQLVariableCoercer.resolveValue(argument.getValue(), variables, variableDefinitions);
                case "where" -> filter = translateFilterValue(
                        GraphQLVariableCoercer.resolveValue(argument.getValue(), variables, variableDefinitions), entity);
                case "onConflict", "conflict" -> conflictColumns = translateConflictColumnsValue(
                        GraphQLVariableCoercer.resolveValue(argument.getValue(), variables, variableDefinitions), entity);
                default -> throw new IllegalStateException(
                        "Mutation argument '" + argument.getName() + "' is not supported.");
            }
        }

        if ((kind == MutationKind.CREATE || kind == MutationKind.UPSERT) && input == null)
            throw new IllegalStateException("Create/upsert mutations require an 'input' object.");
        if (kind == MutationKind.UPDATE && input == null)
            throw new IllegalStateException("Update mutations require an 'input' object.");
        if ((kind == MutationKind.UPDATE || kind == MutationKind.DELETE) && filter == null)
            throw new IllegalStateException(kindName(kind) + " mutations require a 'where' filter.");
        if (kind == MutationKind.DELETE && input != null)
            throw new IllegalStateException("Delete mutations cannot contain an 'input' object.");

        var fields = new ArrayList<MutationFieldValue>();
        var nested = new ArrayList<NestedChildInput>();

        if (input != null) {
            var dictionary = asObject(input);
            if (dictionary == null) throw new IllegalStateException("Mutation 'input' must resolve to an input object.");
            translateInputObject(entity, dictionary, fields, nested);
        }

        return new MutationBuildResult(fields, filter, conflictColumns, nested);
    }

    /** C# prints the enum member name ({@code Update}, {@code Delete}). */
    private static String kindName(MutationKind kind) {
        var lower = kind.name().toLowerCase(Locale.ROOT);
        return Character.toUpperCase(lower.charAt(0)) + lower.substring(1);
    }

    private IMutationIntent buildMutation(SemanticEntity entity, MutationKind kind, MutationBuildResult input) {
        return switch (kind) {
            case CREATE -> new MutationIntent(entity.id(), kind, input.fields(), null, defaultReturnFields(entity));
            case UPDATE -> new MutationIntent(
                    entity.id(), kind, input.fields(), input.filter(), defaultReturnFields(entity));
            case DELETE -> new MutationIntent(
                    entity.id(), kind, List.of(), input.filter(), defaultReturnFields(entity));
            case UPSERT -> new UpsertIntent(
                    entity.id(), input.fields(), input.conflictColumns(), defaultReturnFields(entity));
        };
    }

    private void translateInputObject(
            SemanticEntity entity,
            Map<String, Object> input,
            List<MutationFieldValue> fields,
            List<NestedChildInput> nested) {
        for (var pair : input.entrySet()) {
            var key = pair.getKey();
            var semanticField = GraphQLSemanticAdapter.findField(entity, key);
            if (semanticField != null) {
                var metadataField = metadataField(entity, semanticField.id(),
                        "Field '" + key + "' is not mapped in metadata for '" + entity.name() + "'.");
                var column = metadataField.column();
                if (column == null)
                    throw new IllegalStateException(
                            "Field '" + key + "' on '" + entity.name() + "' has no storage column mapping.");

                fields.add(new MutationFieldValue(
                        column.columnId(),
                        GraphQLMutationValueCoercer.coerce(metadataField, pair.getValue(), entity.name() + "." + key)));
                continue;
            }

            SemanticRelationship relationship = null;
            for (var candidate : entity.relationships())
                if (GraphQLSemanticAdapter.namesEqual(candidate.name(), key)) {
                    relationship = candidate;
                    break;
                }

            if (relationship == null)
                throw new IllegalStateException(
                        "Mutation input field '" + key + "' is not defined on '" + entity.name() + "'.");

            for (var childInput : readNestedInputs(pair.getValue(), relationship)) {
                var childFields = new ArrayList<MutationFieldValue>();
                var grandChildren = new ArrayList<NestedChildInput>();
                var target = model.get(relationship.target());
                translateInputObject(target, childInput, childFields, grandChildren);

                var childIntent = new MutationIntent(
                        target.id(), MutationKind.CREATE, childFields, null, defaultReturnFields(target));

                var grandChildIntents = new ArrayList<NestedMutationChild>();
                for (var grandChild : grandChildren)
                    grandChildIntents.add(new NestedMutationChild(grandChild.relationship().id(), grandChild.intent()));

                nested.add(new NestedChildInput(relationship, new NestedMutationIntent(childIntent, grandChildIntents)));
            }
        }
    }

    private FieldMetadata metadataField(SemanticEntity entity, FieldId id, String missingMessage) {
        for (var candidate : metadata.getEntity(entity.id()).effectiveFields())
            if (candidate.id().equals(id)) return candidate;
        throw new IllegalStateException(missingMessage);
    }

    private static List<Map<String, Object>> readNestedInputs(Object value, SemanticRelationship relationship) {
        if (relationship.cardinality() == RelationshipCardinality.MANY) {
            var list = asList(value);
            if (list == null)
                throw new IllegalStateException(
                        "Collection relationship '" + relationship.name() + "' requires a list input.");
            var result = new ArrayList<Map<String, Object>>(list.size());
            for (var item : list) result.add(requireInputObject(item));
            return result;
        }
        return List.of(requireInputObject(value));
    }

    private static Map<String, Object> requireInputObject(Object value) {
        var map = asObject(value);
        if (map == null) throw new IllegalStateException("Nested mutation input must be an object.");
        return map;
    }

    private static List<FieldId> defaultReturnFields(SemanticEntity entity) {
        var result = new ArrayList<FieldId>();
        result.add(entity.identity().fieldId());
        for (var field : entity.fields()) result.add(field.id());
        return result;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Filter and conflict columns
    // ---------------------------------------------------------------------------------------------------------------

    private SemanticFilterExpression translateFilterValue(Object value, SemanticEntity entity) {
        var dictionary = asObject(value);
        if (dictionary == null) throw new IllegalStateException("Mutation 'where' must be an input object.");

        var expressions = new ArrayList<SemanticFilterExpression>();
        for (var pair : dictionary.entrySet()) {
            var key = pair.getKey();
            var semanticField = GraphQLSemanticAdapter.findField(entity, key);
            if (semanticField == null)
                throw new IllegalStateException(
                        "Mutation filter field '" + key + "' is not defined on '" + entity.name() + "'.");

            var metadataField = metadataField(entity, semanticField.id(),
                    "Filter field '" + key + "' is not mapped in metadata for '" + entity.name() + "'.");

            var operators = asObject(pair.getValue());
            if (operators != null) {
                for (var op : operators.entrySet()) {
                    var filterOperator = parseFilterOperator(op.getKey());
                    var list = asList(op.getValue());
                    Object filterValue;
                    if (list != null && op.getKey().equalsIgnoreCase("in")) {
                        var coerced = new ArrayList<Object>(list.size());
                        for (int i = 0; i < list.size(); i++)
                            coerced.add(GraphQLMutationValueCoercer.coerce(
                                    metadataField, list.get(i), entity.name() + "." + key + "." + op.getKey() + "[" + i + "]"));
                        filterValue = coerced;
                    } else {
                        filterValue = GraphQLMutationValueCoercer.coerce(
                                metadataField, op.getValue(), entity.name() + "." + key + "." + op.getKey());
                    }
                    expressions.add(new SemanticFieldFilter(semanticField.id(), filterOperator, filterValue));
                }
            } else {
                expressions.add(new SemanticFieldFilter(
                        semanticField.id(),
                        SemanticFilterOperator.EQ,
                        GraphQLMutationValueCoercer.coerce(metadataField, pair.getValue(), entity.name() + "." + key)));
            }
        }

        if (expressions.isEmpty()) throw new IllegalStateException("Mutation 'where' cannot be empty.");
        return expressions.size() == 1 ? expressions.get(0) : new SemanticAndFilter(expressions);
    }

    private static SemanticFilterOperator parseFilterOperator(String value) {
        return switch (value.toLowerCase(Locale.ROOT)) {
            case "eq" -> SemanticFilterOperator.EQ;
            case "neq" -> SemanticFilterOperator.NEQ;
            case "in" -> SemanticFilterOperator.IN;
            default -> throw new IllegalStateException("Mutation filter operator '" + value + "' is not supported.");
        };
    }

    private List<ColumnId> translateConflictColumnsValue(Object value, SemanticEntity entity) {
        List<String> names = new ArrayList<>();
        if (value instanceof String single) {
            names.add(single);
        } else {
            var list = asList(value);
            if (list == null)
                throw new IllegalStateException("Mutation conflict columns must be a string or list of strings.");
            for (var item : list) {
                if (!(item instanceof String text))
                    throw new IllegalStateException("Mutation conflict columns must contain only strings.");
                names.add(text);
            }
        }

        var columns = new ArrayList<ColumnId>(names.size());
        for (var name : names) {
            var field = GraphQLSemanticAdapter.findField(entity, name);
            if (field == null)
                throw new IllegalStateException("Conflict field '" + name + "' is not defined on '" + entity.name() + "'.");

            var metadataField = metadataField(entity, field.id(), "Conflict field '" + name + "' has no metadata mapping.");
            if (metadataField.column() == null)
                throw new IllegalStateException("Conflict field '" + name + "' has no storage column.");
            columns.add(metadataField.column().columnId());
        }
        return List.copyOf(columns);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Return shape
    // ---------------------------------------------------------------------------------------------------------------

    private GraphQLMutationResultShape translateReturnShape(
            SemanticEntity entity,
            SelectionSet selectionSet,
            Map<String, FragmentDefinition> fragments,
            Map<String, Object> variables,
            Map<String, VariableDefinition> variableDefinitions,
            Set<String> fragmentStack) {
        if (selectionSet == null) return new GraphQLMutationResultShape(List.of(), List.of());

        var fields = new ArrayList<GraphQLMutationResultField>();
        var relationships = new ArrayList<GraphQLMutationResultRelationship>();
        var responseNames = new HashSet<String>();

        for (Selection<?> selection : selectionSet.getSelections()) {
            if (selection instanceof Field field) {
                if (!GraphQLDirectiveEvaluator.shouldInclude(field.getDirectives(), variables, variableDefinitions))
                    continue;
                if (field.getName().startsWith("__")) continue;

                if (field.getSelectionSet() != null) {
                    SemanticRelationship relationship = null;
                    for (var candidate : entity.relationships())
                        if (GraphQLSemanticAdapter.namesEqual(candidate.name(), field.getName())) {
                            relationship = candidate;
                            break;
                        }
                    if (relationship == null)
                        throw new IllegalStateException("Mutation result field '" + field.getName()
                                + "' is not a relationship on '" + entity.name() + "'.");

                    var target = model.get(relationship.target());
                    var childShape = translateReturnShape(
                            target, field.getSelectionSet(), fragments, variables, variableDefinitions, fragmentStack);

                    var responseName = field.getAlias() != null
                            ? field.getAlias()
                            : GraphQLSemanticAdapter.toGraphQLName(relationship.name());
                    addMutationRelationship(
                            relationships,
                            responseNames,
                            new GraphQLMutationResultRelationship(
                                    relationship.id(),
                                    GraphQLSemanticAdapter.toGraphQLName(relationship.name()),
                                    responseName,
                                    childShape,
                                    relationship.cardinality() == RelationshipCardinality.MANY));
                    continue;
                }

                var scalar = GraphQLSemanticAdapter.findField(entity, field.getName());
                if (scalar == null)
                    throw new IllegalStateException("Mutation result field '" + field.getName()
                            + "' is not defined on '" + entity.name() + "'.");

                var scalarResponseName = field.getAlias() != null
                        ? field.getAlias()
                        : GraphQLSemanticAdapter.toGraphQLName(field.getName());
                addMutationField(
                        fields,
                        responseNames,
                        new GraphQLMutationResultField(scalar.id(), scalarResponseName, scalarResponseName));
            } else if (selection instanceof InlineFragment inline) {
                if (!GraphQLDirectiveEvaluator.shouldInclude(inline.getDirectives(), variables, variableDefinitions))
                    continue;
                validateFragmentType(
                        entity,
                        inline.getTypeCondition() == null ? null : inline.getTypeCondition().getName(),
                        "inline fragment");
                if (inline.getSelectionSet() != null) {
                    var nestedShape = translateReturnShape(
                            entity, inline.getSelectionSet(), fragments, variables, variableDefinitions, fragmentStack);
                    mergeShape(fields, relationships, responseNames, nestedShape);
                }
            } else if (selection instanceof FragmentSpread spread) {
                if (!GraphQLDirectiveEvaluator.shouldInclude(spread.getDirectives(), variables, variableDefinitions))
                    continue;
                var fragment = fragments.get(spread.getName());
                if (fragment == null)
                    throw new IllegalStateException("GraphQL fragment '" + spread.getName() + "' was not found.");
                validateFragmentType(entity, fragment.getTypeCondition().getName(), "fragment '" + spread.getName() + "'");

                if (!fragmentStack.add(spread.getName()))
                    throw new IllegalStateException("GraphQL fragment cycle detected at '" + spread.getName() + "'.");

                var fragmentShape = translateReturnShape(
                        entity, fragment.getSelectionSet(), fragments, variables, variableDefinitions, fragmentStack);
                fragmentStack.remove(spread.getName());
                mergeShape(fields, relationships, responseNames, fragmentShape);
            } else {
                throw new IllegalStateException(
                        "GraphQL mutation selection '" + selection.getClass().getSimpleName() + "' is not supported.");
            }
        }

        return new GraphQLMutationResultShape(fields, relationships);
    }

    private static void mergeShape(
            List<GraphQLMutationResultField> fields,
            List<GraphQLMutationResultRelationship> relationships,
            Set<String> responseNames,
            GraphQLMutationResultShape shape) {
        for (var field : shape.fields()) addMutationField(fields, responseNames, field);
        for (var relationship : shape.relationships()) addMutationRelationship(relationships, responseNames, relationship);
    }

    private static void addMutationField(
            List<GraphQLMutationResultField> fields, Set<String> responseNames, GraphQLMutationResultField field) {
        if (!responseNames.add(field.responseName())) {
            for (var existing : fields)
                if (existing.responseName().equals(field.responseName())) {
                    if (existing.field().equals(field.field())) return;
                    break;
                }
            throw new IllegalStateException(
                    "GraphQL result contains duplicate response alias/name '" + field.responseName() + "'.");
        }
        fields.add(field);
    }

    private static void addMutationRelationship(
            List<GraphQLMutationResultRelationship> relationships,
            Set<String> responseNames,
            GraphQLMutationResultRelationship relationship) {
        if (!responseNames.add(relationship.responseName())) {
            for (var existing : relationships)
                if (existing.responseName().equals(relationship.responseName())) {
                    if (existing.relationship().equals(relationship.relationship())) return;
                    break;
                }
            throw new IllegalStateException(
                    "GraphQL result contains duplicate response alias/name '" + relationship.responseName() + "'.");
        }
        relationships.add(relationship);
    }

    private static void validateFragmentType(SemanticEntity entity, String typeCondition, String context) {
        if (typeCondition == null) return;
        if (!entity.name().equalsIgnoreCase(typeCondition))
            throw new IllegalStateException("GraphQL " + context + " targets '" + typeCondition
                    + "' and cannot be applied to selection type '" + entity.name() + "'.");
    }

    /**
     * Narrows each mutation's return fields to what the GraphQL selection requested (plus the identity when children
     * need it for key propagation).
     *
     * <p>Parity note: like the C# original, only the FIRST nested child per relationship is carried over when that
     * relationship is also requested in the result; further children for the same relationship are not retained.
     */
    private NestedMutationIntent applyReturnShape(NestedMutationIntent intent, GraphQLMutationResultShape shape) {
        var requestedFields = new ArrayList<FieldId>();
        for (var field : shape.fields()) requestedFields.add(field.field());
        if (!intent.children().isEmpty()) {
            var identity = model.get(intent.mutation().entity()).identity().fieldId();
            if (!requestedFields.contains(identity)) requestedFields.add(0, identity);
        }

        var mutation = applyReturnFields(intent.mutation(), requestedFields);
        var children = new ArrayList<NestedMutationChild>();

        for (var relationship : shape.relationships()) {
            NestedMutationChild child = null;
            for (var candidate : intent.children())
                if (candidate.relationship().equals(relationship.relationship())) {
                    child = candidate;
                    break;
                }

            if (child == null) {
                var parentEntity = model.get(intent.mutation().entity());
                SemanticRelationship semanticRelationship = null;
                for (var candidate : parentEntity.relationships())
                    if (candidate.id().equals(relationship.relationship())) {
                        semanticRelationship = candidate;
                        break;
                    }

                if (semanticRelationship == null)
                    throw new IllegalStateException("Mutation result relationship '" + relationship.responseName()
                            + "' is not defined on '" + parentEntity.name() + "'.");

                // A singular relationship may be requested in the result even when the mutation carried no nested
                // mutation: there is no child node to materialize, so the GraphQL result is naturally null.
                // Collection relationships stay strict.
                if (semanticRelationship.cardinality() == RelationshipCardinality.ONE) continue;

                throw new IllegalStateException(
                        "Mutation result relationship '" + relationship.responseName() + "' has no nested mutation.");
            }

            children.add(new NestedMutationChild(
                    relationship.relationship(), applyReturnShape(child.mutation(), relationship.shape())));
        }

        for (var child : intent.children()) {
            boolean inShape = false;
            for (var relationship : shape.relationships())
                if (relationship.relationship().equals(child.relationship())) {
                    inShape = true;
                    break;
                }
            if (!inShape) children.add(child);
        }

        return new NestedMutationIntent(mutation, children);
    }

    private static IMutationIntent applyReturnFields(IMutationIntent mutation, List<FieldId> fields) {
        if (mutation instanceof MutationIntent intent)
            return new MutationIntent(intent.entity(), intent.kind(), intent.fields(), intent.filter(), fields);
        if (mutation instanceof UpsertIntent intent)
            return new UpsertIntent(intent.entity(), intent.fields(), intent.conflictColumns(), fields);
        return mutation;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Value shape helpers
    // ---------------------------------------------------------------------------------------------------------------

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
}
