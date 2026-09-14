using System.Diagnostics;
using System.Globalization;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Query;
using HotChocolate.Language;

namespace Foundgine.Extensions.GraphQL.HotChocolate;

/// <summary>
/// Translates GraphQL mutation syntax into provider-neutral Foundgine mutation
/// intents. GraphQL variables, fragments, aliases, directives and output
/// projection remain entirely at this adapter boundary.
/// </summary>
public sealed class HotChocolateMutationAdapter
{
    private readonly SemanticModel _model;
    private readonly IMetadataProvider _metadata;

    public SemanticModel Model => _model;

    public HotChocolateMutationAdapter(SemanticModel model, IMetadataProvider metadata)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    public NestedMutationIntent Adapt(string graphql) =>
        AdaptResultShape(graphql).Intent;

    public NestedMutationIntent Adapt(
        string graphql,
        string? operationName,
        IReadOnlyDictionary<string, object?>? variables = null) =>
        AdaptResultShape(graphql, variables, operationName).Intent;

    public NestedMutationIntent Adapt(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables) =>
        Adapt(graphql, operationName: null, variables);

    public NestedMutationIntent Adapt(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables,
        string? operationName) =>
        Adapt(graphql, operationName, variables);


    public GraphQLMutationAdaptation AdaptWithResultShape(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? operationName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphql);

        var document = Utf8GraphQLParser.Parse(graphql);
        var operation = SelectOperation(document, OperationType.Mutation, operationName);
        var variableDefinitions = operation.VariableDefinitions
            .ToDictionary(x => x.Variable.Name.Value, x => x, StringComparer.Ordinal);
        GraphQLVariableCoercer.ValidateSuppliedVariables(variables, variableDefinitions);

        if (operation.Directives.Count != 0)
            throw new InvalidOperationException(
                "GraphQL mutation operation directives are not supported by the adapter.");

        var fields = operation.SelectionSet.Selections
            .OfType<FieldNode>()
            .Where(x => !x.Name.Value.StartsWith("__", StringComparison.Ordinal))
            .ToArray();

        if (fields.Length != 1)
            throw new InvalidOperationException(
                "Foundgine GraphQL mutation adapter supports exactly one root mutation field.");

        var root = fields[0];
        if (root.Alias is not null)
            throw new InvalidOperationException(
                "GraphQL mutation root aliases are not supported by the mutation contract.");

        return AdaptRootField(root, document, variableDefinitions, variables);
    }

    /// <summary>
    /// Batch form of <see cref="AdaptWithResultShape"/>: accepts a mutation document with
    /// MORE THAN ONE root field, each of which becomes an independent mutation (createX,
    /// updateX, deleteX, upsertX; each may still have its own nested children, same as the
    /// single-field form). Every root field in a batch document MUST be aliased - that alias
    /// is the key used to correlate each result back to its request in the response and in
    /// <see cref="GraphQLMutationBatchItem.ResultKey"/>.
    ///
    /// This does not change the single-field contract: a document with exactly one,
    /// unaliased root field should keep going through <see cref="AdaptWithResultShape"/>.
    /// Combine the returned items' <c>Intent</c> values with
    /// <c>MutationPlanner.Plan(IReadOnlyList&lt;NestedMutationIntent&gt;)</c> to get one
    /// dependency-aware <c>MutationBatchPlan</c> for the whole document.
    /// </summary>
    public IReadOnlyList<GraphQLMutationBatchItem> AdaptBatchWithResultShape(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? operationName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphql);

        var document = Utf8GraphQLParser.Parse(graphql);
        var operation = SelectOperation(document, OperationType.Mutation, operationName);
        var variableDefinitions = operation.VariableDefinitions
            .ToDictionary(x => x.Variable.Name.Value, x => x, StringComparer.Ordinal);
        GraphQLVariableCoercer.ValidateSuppliedVariables(variables, variableDefinitions);

        if (operation.Directives.Count != 0)
            throw new InvalidOperationException(
                "GraphQL mutation operation directives are not supported by the adapter.");

        var fields = operation.SelectionSet.Selections
            .OfType<FieldNode>()
            .Where(x => !x.Name.Value.StartsWith("__", StringComparison.Ordinal))
            .ToArray();

        if (fields.Length == 0)
            throw new InvalidOperationException("GraphQL mutation operation contains no root fields.");

        if (fields.Length == 1 && fields[0].Alias is null)
        {
            // Not actually a batch - route through the single-field contract so callers
            // that always call the batch method still get the plain, unkeyed behavior.
            return
            [
                new GraphQLMutationBatchItem(fields[0].Name.Value,
                    AdaptRootField(fields[0], document, variableDefinitions, variables))
            ];
        }

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<GraphQLMutationBatchItem>(fields.Length);
        foreach (var field in fields)
        {
            if (field.Alias is null)
                throw new InvalidOperationException(
                    $"Root mutation field '{field.Name.Value}' must be aliased when a mutation document " +
                    "contains more than one root field, so each result can be correlated back to its request.");

            var key = field.Alias.Value;
            if (!seenKeys.Add(key))
                throw new InvalidOperationException($"Duplicate root mutation alias '{key}' in batch document.");

            items.Add(
                new GraphQLMutationBatchItem(key, AdaptRootField(field, document, variableDefinitions, variables)));
        }

        return items;
    }

    public GraphQLAdapterResult<IReadOnlyList<GraphQLMutationBatchItem>> TryAdaptBatch(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? operationName = null)
    {
        try
        {
            return GraphQLAdapterResult<IReadOnlyList<GraphQLMutationBatchItem>>.Success(
                AdaptBatchWithResultShape(graphql, variables, operationName));
        }
        catch (Exception exception)
        {
            return GraphQLAdapterResult<IReadOnlyList<GraphQLMutationBatchItem>>.Failure(
                GraphQLAdapterErrors.FromException(exception));
        }
    }

    private GraphQLMutationAdaptation AdaptRootField(
        FieldNode root,
        DocumentNode document,
        IReadOnlyDictionary<string, VariableDefinitionNode> variableDefinitions,
        IReadOnlyDictionary<string, object?>? variables)
    {
        if (root.Directives.Count != 0)
            throw new InvalidOperationException(
                "GraphQL mutation directives on the root mutation field are not supported by the adapter.");

        var (kind, entityName) = ParseOperation(root.Name.Value);
        var entity = FindEntity(entityName);
        var input = ReadInput(root, kind, variables, variableDefinitions);

        var rootMutation = BuildMutation(entity, kind, input);
        var nestedIntent = new NestedMutationIntent(
            rootMutation,
            input.NestedChildren
                .Select(x => new NestedMutationChild(x.Relationship.Id, x.Intent))
                .ToArray());

        var resultShape = TranslateReturnShape(
            entity,
            root.SelectionSet,
            document.Definitions.OfType<FragmentDefinitionNode>()
                .ToDictionary(x => x.Name.Value, x => x, StringComparer.Ordinal),
            variables,
            variableDefinitions,
            new HashSet<string>(StringComparer.Ordinal));

        nestedIntent = ApplyReturnShape(nestedIntent, resultShape);

        return new GraphQLMutationAdaptation(nestedIntent, resultShape);
    }

    public GraphQLMutationAdaptation AdaptResultShape(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? operationName = null) =>
        AdaptWithResultShape(graphql, variables, operationName);

    public GraphQLAdapterResult<NestedMutationIntent> TryAdapt(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? operationName = null)
    {
        try
        {
            return GraphQLAdapterResult<NestedMutationIntent>.Success(
                Adapt(graphql, operationName, variables));
        }
        catch (Exception exception)
        {
            return GraphQLAdapterResult<NestedMutationIntent>.Failure(
                GraphQLAdapterErrors.FromException(exception));
        }
    }

    public GraphQLAdapterResult<NestedMutationIntent> TryAdapt(
        string graphql,
        string? operationName,
        IReadOnlyDictionary<string, object?>? variables) =>
        TryAdapt(graphql, variables, operationName);

    private static OperationDefinitionNode SelectOperation(
        DocumentNode document,
        OperationType expectedType,
        string? operationName)
    {
        var operations = document.Definitions.OfType<OperationDefinitionNode>().ToArray();
        if (operations.Length == 0)
            throw new InvalidOperationException("GraphQL document contains no operation definitions.");

        OperationDefinitionNode operation;
        if (operationName is not null)
        {
            operation = operations.FirstOrDefault(x =>
                            string.Equals(x.Name?.Value, operationName, StringComparison.Ordinal))
                        ?? throw new InvalidOperationException(
                            $"GraphQL operation '{operationName}' was not found.");
        }
        else
        {
            if (operations.Length > 1)
                throw new InvalidOperationException(
                    "GraphQL document contains multiple operations; an operation name is required.");
            operation = operations[0];
        }

        if (operation.Operation != expectedType)
            throw new InvalidOperationException(
                $"Selected GraphQL operation must be a mutation operation.");

        return operation;
    }

    private MutationBuildResult ReadInput(
        FieldNode field,
        MutationKind kind,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, VariableDefinitionNode> variableDefinitions)
    {
        var entity = FindEntity(ParseOperation(field.Name.Value).EntityName);
        object? input = null;
        SemanticFilterExpression? filter = null;
        IReadOnlyList<ColumnId>? conflictColumns = null;

        foreach (var argument in field.Arguments)
        {
            switch (argument.Name.Value)
            {
                case "input":
                    input = ResolveValue(argument.Value, variables, variableDefinitions);
                    break;

                case "where":
                    filter = TranslateFilterValue(
                        ResolveValue(argument.Value, variables, variableDefinitions),
                        entity);
                    break;

                case "onConflict":
                case "conflict":
                    conflictColumns = TranslateConflictColumnsValue(
                        ResolveValue(argument.Value, variables, variableDefinitions),
                        entity);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Mutation argument '{argument.Name.Value}' is not supported.");
            }
        }

        if (kind is MutationKind.Create or MutationKind.Upsert && input is null)
            throw new InvalidOperationException("Create/upsert mutations require an 'input' object.");
        if (kind is MutationKind.Update && input is null)
            throw new InvalidOperationException("Update mutations require an 'input' object.");
        if (kind is MutationKind.Update or MutationKind.Delete && filter is null)
            throw new InvalidOperationException($"{kind} mutations require a 'where' filter.");
        if (kind == MutationKind.Delete && input is not null)
            throw new InvalidOperationException("Delete mutations cannot contain an 'input' object.");

        var fields = new List<MutationFieldValue>();
        var nested = new List<NestedChildInput>();

        if (input is not null)
        {
            if (input is not IReadOnlyDictionary<string, object?> dictionary)
                throw new InvalidOperationException("Mutation 'input' must resolve to an input object.");
            TranslateInputObject(entity, dictionary, fields, nested);
        }

        return new MutationBuildResult(fields, filter, conflictColumns, nested);
    }

    private IMutationIntent BuildMutation(
        SemanticEntity entity,
        MutationKind kind,
        MutationBuildResult input)
    {
        return kind switch
        {
            MutationKind.Create => new MutationIntent(entity.Id, kind, input.Fields, null, DefaultReturnFields(entity)),
            MutationKind.Update => new MutationIntent(entity.Id, kind, input.Fields, input.Filter,
                DefaultReturnFields(entity)),
            MutationKind.Delete => new MutationIntent(entity.Id, kind, [], input.Filter, DefaultReturnFields(entity)),
            MutationKind.Upsert => new UpsertIntent(entity.Id, input.Fields, input.ConflictColumns,
                DefaultReturnFields(entity)),
            _ => throw new InvalidOperationException($"Mutation '{kind}' is not supported.")
        };
    }

    private void TranslateInputObject(
        SemanticEntity entity,
        IReadOnlyDictionary<string, object?> input,
        List<MutationFieldValue> fields,
        List<NestedChildInput> nested)
    {
        foreach (var pair in input)
        {
            var semanticField = FindField(entity, pair.Key);
            if (semanticField is not null)
            {
                var metadataEntity = _metadata.GetEntity(entity.Id);
                var metadataField = metadataEntity.EffectiveFields.FirstOrDefault(x => x.Id == semanticField.Id)
                                    ?? throw new InvalidOperationException(
                                        $"Field '{pair.Key}' is not mapped in metadata for '{entity.Name}'.");

                var value = pair.Value;
                var column = metadataField.Column
                             ?? throw new InvalidOperationException(
                                 $"Field '{pair.Key}' on '{entity.Name}' has no storage column mapping.");

                fields.Add(new MutationFieldValue(
                    column.ColumnId,
                    CoerceMutationValue(metadataField, value, $"{entity.Name}.{pair.Key}")));
                continue;
            }

            var relationship = entity.Relationships.FirstOrDefault(x => NamesEqual(x.Name, pair.Key));

            if (relationship is null)
                throw new InvalidOperationException(
                    $"Mutation input field '{pair.Key}' is not defined on '{entity.Name}'.");

            foreach (var childInput in ReadNestedInputs(pair.Value, relationship))
            {
                var childFields = new List<MutationFieldValue>();
                var grandChildren = new List<NestedChildInput>();
                TranslateInputObject(_model.Get(relationship.Target), childInput, childFields, grandChildren);

                var target = _model.Get(relationship.Target);
                var childIntent = new MutationIntent(
                    target.Id,
                    MutationKind.Create,
                    childFields,
                    null,
                    DefaultReturnFields(target));

                nested.Add(new NestedChildInput(
                    relationship,
                    new NestedMutationIntent(
                        childIntent,
                        grandChildren
                            .Select(x => new NestedMutationChild(x.Relationship.Id, x.Intent))
                            .ToArray())));
            }
        }
    }

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> ReadNestedInputs(
        object? value,
        SemanticRelationship relationship)
    {
        if (relationship.Cardinality == RelationshipCardinality.Many)
        {
            if (value is not System.Collections.IEnumerable enumerable || value is string ||
                value is IReadOnlyDictionary<string, object?>)
            {
                throw new InvalidOperationException(
                    $"Collection relationship '{relationship.Name}' requires a list input.");
            }

            return enumerable.Cast<object?>()
                .Select(RequireInputObject)
                .ToArray();
        }

        return [RequireInputObject(value)];
    }

    private static IReadOnlyDictionary<string, object?> RequireInputObject(object? value) =>
        value as IReadOnlyDictionary<string, object?>
        ?? throw new InvalidOperationException("Nested mutation input must be an object.");

    private static IReadOnlyList<FieldId> DefaultReturnFields(SemanticEntity entity) =>
        [entity.Identity.FieldId, .. entity.Fields.Select(x => x.Id)];

    private GraphQLMutationResultShape TranslateReturnShape(
        SemanticEntity entity,
        SelectionSetNode? selectionSet,
        IReadOnlyDictionary<string, FragmentDefinitionNode> fragments,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, VariableDefinitionNode> variableDefinitions,
        HashSet<string> fragmentStack)
    {
        if (selectionSet is null)
            return new GraphQLMutationResultShape([], []);

        var fields = new List<GraphQLMutationResultField>();
        var relationships = new List<GraphQLMutationResultRelationship>();
        var responseNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var selection in selectionSet.Selections)
        {
            switch (selection)
            {
                case FieldNode field:
                    if (!ShouldInclude(field.Directives, variables, variableDefinitions))
                        continue;

                    if (field.Name.Value.StartsWith("__", StringComparison.Ordinal))
                        continue;

                    if (field.SelectionSet is not null)
                    {
                        var relationship =
                            entity.Relationships.FirstOrDefault(x => NamesEqual(x.Name, field.Name.Value))
                            ?? throw new InvalidOperationException(
                                $"Mutation result field '{field.Name.Value}' is not a relationship on '{entity.Name}'.");

                        var target = _model.Get(relationship.Target);
                        var childShape = TranslateReturnShape(
                            target, field.SelectionSet, fragments, variables,
                            variableDefinitions, fragmentStack);

                        var responseName = field.Alias?.Value ?? ToGraphQLName(relationship.Name);
                        AddMutationRelationship(
                            relationships, responseNames,
                            new GraphQLMutationResultRelationship(
                                relationship.Id,
                                ToGraphQLName(relationship.Name),
                                responseName,
                                childShape,
                                relationship.Cardinality == RelationshipCardinality.Many));
                        continue;
                    }

                    var scalar = FindField(entity, field.Name.Value)
                                 ?? throw new InvalidOperationException(
                                     $"Mutation result field '{field.Name.Value}' is not defined on '{entity.Name}'.");

                    var scalarResponseName = field.Alias?.Value ?? ToGraphQLName(field.Name.Value);
                    AddMutationField(
                        fields, responseNames,
                        new GraphQLMutationResultField(
                            scalar.Id,
                            scalarResponseName,
                            scalarResponseName));
                    break;

                case InlineFragmentNode inlineFragment:
                    if (!ShouldInclude(inlineFragment.Directives, variables, variableDefinitions))
                        continue;
                    ValidateFragmentType(entity, inlineFragment.TypeCondition?.Name.Value, "inline fragment");
                    if (inlineFragment.SelectionSet is not null)
                    {
                        var nestedShape = TranslateReturnShape(
                            entity, inlineFragment.SelectionSet, fragments, variables,
                            variableDefinitions, fragmentStack);
                        MergeShape(fields, relationships, responseNames, nestedShape);
                    }

                    break;

                case FragmentSpreadNode spread:
                    if (!ShouldInclude(spread.Directives, variables, variableDefinitions))
                        continue;
                    if (!fragments.TryGetValue(spread.Name.Value, out var fragment))
                        throw new InvalidOperationException(
                            $"GraphQL fragment '{spread.Name.Value}' was not found.");
                    ValidateFragmentType(entity, fragment.TypeCondition.Name.Value, $"fragment '{spread.Name.Value}'");

                    if (!fragmentStack.Add(spread.Name.Value))
                        throw new InvalidOperationException(
                            $"GraphQL fragment cycle detected at '{spread.Name.Value}'.");

                    var fragmentShape = TranslateReturnShape(
                        entity, fragment.SelectionSet, fragments, variables,
                        variableDefinitions, fragmentStack);
                    fragmentStack.Remove(spread.Name.Value);
                    MergeShape(fields, relationships, responseNames, fragmentShape);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"GraphQL mutation selection '{selection.GetType().Name}' is not supported.");
            }
        }

        return new GraphQLMutationResultShape(fields, relationships);
    }

    private static void MergeShape(
        List<GraphQLMutationResultField> fields,
        List<GraphQLMutationResultRelationship> relationships,
        HashSet<string> responseNames,
        GraphQLMutationResultShape shape)
    {
        foreach (var field in shape.Fields)
            AddMutationField(fields, responseNames, field);

        foreach (var relationship in shape.Relationships)
            AddMutationRelationship(relationships, responseNames, relationship);
    }

    private static void AddMutationField(
        List<GraphQLMutationResultField> fields,
        HashSet<string> responseNames,
        GraphQLMutationResultField field)
    {
        if (!responseNames.Add(field.ResponseName))
        {
            var existing = fields.FirstOrDefault(x => x.ResponseName == field.ResponseName);
            if (existing is not null && existing.Field == field.Field)
                return;

            throw new InvalidOperationException(
                $"GraphQL result contains duplicate response alias/name '{field.ResponseName}'.");
        }

        fields.Add(field);
    }

    private static void AddMutationRelationship(
        List<GraphQLMutationResultRelationship> relationships,
        HashSet<string> responseNames,
        GraphQLMutationResultRelationship relationship)
    {
        if (!responseNames.Add(relationship.ResponseName))
        {
            var existing = relationships.FirstOrDefault(x => x.ResponseName == relationship.ResponseName);
            if (existing is not null && existing.Relationship == relationship.Relationship)
                return;

            throw new InvalidOperationException(
                $"GraphQL result contains duplicate response alias/name '{relationship.ResponseName}'.");
        }

        relationships.Add(relationship);
    }

    private NestedMutationIntent ApplyReturnShape(
        NestedMutationIntent intent,
        GraphQLMutationResultShape shape)
    {
        var requestedFields = shape.Fields.Select(x => x.Field).ToList();
        if (intent.Children.Count != 0)
        {
            var identity = _model.Get(intent.Mutation.Entity).Identity.FieldId;
            if (!requestedFields.Contains(identity))
                requestedFields.Insert(0, identity);
        }

        var mutation = ApplyReturnFields(intent.Mutation, requestedFields);
        var children = new List<NestedMutationChild>();

        foreach (var relationship in shape.Relationships)
        {
            var child = intent.Children.FirstOrDefault(x => x.Relationship == relationship.Relationship);
            if (child is null)
            {
                var parentEntity = _model.Get(intent.Mutation.Entity);
                var semanticRelationship = parentEntity.Relationships
                    .FirstOrDefault(x => x.Id == relationship.Relationship);

                if (semanticRelationship is null)
                    throw new InvalidOperationException(
                        $"Mutation result relationship '{relationship.ResponseName}' is not defined on '{parentEntity.Name}'.");

                // A singular relationship may be requested in the GraphQL result
                // even when the mutation did not contain a nested mutation. There
                // is then no child node to materialize, so the GraphQL result is
                // naturally null. Collection relationships remain strict: without
                // a nested mutation there is no collection operation to execute.
                if (semanticRelationship.Cardinality == RelationshipCardinality.One)
                    continue;

                throw new InvalidOperationException(
                    $"Mutation result relationship '{relationship.ResponseName}' has no nested mutation.");
            }

            children.Add(new NestedMutationChild(
                relationship.Relationship,
                ApplyReturnShape(child.Mutation, relationship.Shape)));
        }

        foreach (var child in intent.Children)
        {
            if (!shape.Relationships.Any(x => x.Relationship == child.Relationship))
                children.Add(child);
        }

        return new NestedMutationIntent(mutation, children);
    }

    private static IMutationIntent ApplyReturnFields(
        IMutationIntent mutation,
        IReadOnlyList<FieldId> fields)
    {
        return mutation switch
        {
            MutationIntent intent => intent with { ReturnFields = fields },
            UpsertIntent intent => intent with { ReturnFields = fields },
            _ => mutation
        };
    }

    private SemanticFilterExpression TranslateFilterValue(
        object? value,
        SemanticEntity entity)
    {
        if (value is not IReadOnlyDictionary<string, object?> dictionary)
            throw new InvalidOperationException("Mutation 'where' must be an input object.");

        var expressions = new List<SemanticFilterExpression>();
        foreach (var pair in dictionary)
        {
            var semanticField = FindField(entity, pair.Key)
                                ?? throw new InvalidOperationException(
                                    $"Mutation filter field '{pair.Key}' is not defined on '{entity.Name}'.");

            var metadataEntity = _metadata.GetEntity(entity.Id);
            var metadataField = metadataEntity.EffectiveFields.FirstOrDefault(x => x.Id == semanticField.Id)
                                ?? throw new InvalidOperationException(
                                    $"Filter field '{pair.Key}' is not mapped in metadata for '{entity.Name}'.");

            if (pair.Value is IReadOnlyDictionary<string, object?> operators)
            {
                foreach (var op in operators)
                {
                    var filterOperator = ParseFilterOperator(op.Key);
                    var filterValue = op.Value is System.Collections.IEnumerable list &&
                                      op.Value is not string &&
                                      op.Key.Equals("in", StringComparison.OrdinalIgnoreCase)
                        ? list.Cast<object?>()
                            .Select((item, index) => CoerceMutationValue(
                                metadataField,
                                item,
                                $"{entity.Name}.{pair.Key}.{op.Key}[{index}]"))
                            .ToArray()
                        : CoerceMutationValue(
                            metadataField,
                            op.Value,
                            $"{entity.Name}.{pair.Key}.{op.Key}");

                    expressions.Add(new SemanticFieldFilter(
                        semanticField.Id,
                        filterOperator,
                        filterValue));
                }
            }
            else
            {
                expressions.Add(new SemanticFieldFilter(
                    semanticField.Id,
                    SemanticFilterOperator.Eq,
                    CoerceMutationValue(metadataField, pair.Value, $"{entity.Name}.{pair.Key}")));
            }
        }

        if (expressions.Count == 0)
            throw new InvalidOperationException("Mutation 'where' cannot be empty.");

        return expressions.Count == 1
            ? expressions[0]
            : new SemanticAndFilter(expressions);
    }

    private static SemanticFilterOperator ParseFilterOperator(string value) =>
        value.ToLowerInvariant() switch
        {
            "eq" => SemanticFilterOperator.Eq,
            "neq" => SemanticFilterOperator.Neq,
            "in" => SemanticFilterOperator.In,
            _ => throw new InvalidOperationException(
                $"Mutation filter operator '{value}' is not supported.")
        };

    private IReadOnlyList<ColumnId> TranslateConflictColumnsValue(
        object? value,
        SemanticEntity entity)
    {
        IEnumerable<string> names = value switch
        {
            string single => [single],
            System.Collections.IEnumerable list when value is not string =>
                list.Cast<object?>().Select(x =>
                    x as string ?? throw new InvalidOperationException(
                        "Mutation conflict columns must contain only strings.")),
            _ => throw new InvalidOperationException(
                "Mutation conflict columns must be a string or list of strings.")
        };

        var metadataEntity = _metadata.GetEntity(entity.Id);
        return names.Select(name =>
        {
            var field = FindField(entity, name)
                        ?? throw new InvalidOperationException(
                            $"Conflict field '{name}' is not defined on '{entity.Name}'.");

            var metadataField = metadataEntity.EffectiveFields.FirstOrDefault(x => x.Id == field.Id)
                                ?? throw new InvalidOperationException(
                                    $"Conflict field '{name}' has no metadata mapping.");

            return metadataField.Column?.ColumnId
                   ?? throw new InvalidOperationException(
                       $"Conflict field '{name}' has no storage column.");
        }).ToArray();
    }

    private SemanticField? FindField(SemanticEntity entity, string name)
    {
        var field = entity.Fields.FirstOrDefault(x => NamesEqual(x.Name, name));
        if (field is not null)
            return field;

        if (NamesEqual(entity.Identity.Name, name) ||
            string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
            return new SemanticField(entity.Identity.FieldId, entity.Identity.Name, typeof(object));

        return null;
    }

    private static object? CoerceMutationValue(
        FieldMetadata field,
        object? value,
        string path)
    {
        var declaredType = field.ClrType;
        var nullableType = Nullable.GetUnderlyingType(declaredType);
        var type = nullableType ?? declaredType;

        if (value is null)
        {
            if (!declaredType.IsValueType || nullableType is not null)
                return null;

            throw new InvalidOperationException(
                $"GraphQL value at '{path}' cannot be null.");
        }

        if (type.IsInstanceOfType(value))
            return value;

        try
        {
            if (type == typeof(string))
                throw TypeError(path, type, value);

            if (type == typeof(Guid))
            {
                if (value is string text && Guid.TryParse(text, out var guid))
                    return guid;

                throw TypeError(path, type, value);
            }

            if (type.IsEnum)
            {
                if (value is string enumName && Enum.TryParse(type, enumName, true, out var enumValue))
                    return enumValue;

                throw TypeError(path, type, value);
            }

            if (type == typeof(DateTime) && value is string dateTimeText)
                return DateTime.Parse(dateTimeText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            if (type == typeof(DateTimeOffset) && value is string dateTimeOffsetText)
                return DateTimeOffset.Parse(dateTimeOffsetText, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);

            if (type == typeof(DateOnly) && value is string dateOnlyText)
                return DateOnly.Parse(dateOnlyText, CultureInfo.InvariantCulture);

            if (type == typeof(TimeOnly) && value is string timeOnlyText)
                return TimeOnly.Parse(timeOnlyText, CultureInfo.InvariantCulture);

            if (type == typeof(bool) ||
                type == typeof(byte) || type == typeof(sbyte) ||
                type == typeof(short) || type == typeof(ushort) ||
                type == typeof(int) || type == typeof(uint) ||
                type == typeof(long) || type == typeof(ulong) ||
                type == typeof(float) || type == typeof(double) ||
                type == typeof(decimal))
            {
                return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            }
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"GraphQL value for '{path}' could not be converted to '{type.Name}'.", exception);
        }

        throw TypeError(path, type, value);
    }

    private static InvalidOperationException TypeError(string path, Type expectedType, object value) =>
        new($"GraphQL value for '{path}' expects '{expectedType.Name}', but received '{value.GetType().Name}'.");

    private static object? ResolveValue(
        IValueNode value,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, VariableDefinitionNode> definitions) =>
        GraphQLVariableCoercer.ResolveValue(value, variables, definitions);

    private static bool ShouldInclude(
        IReadOnlyList<DirectiveNode> directives,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, VariableDefinitionNode> definitions) =>
        GraphQLDirectiveEvaluator.ShouldInclude(directives, variables, definitions);

    private static void ValidateFragmentType(
        SemanticEntity entity,
        string? typeCondition,
        string context)
    {
        if (typeCondition is null)
            return;

        if (!string.Equals(entity.Name, typeCondition, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"GraphQL {context} targets '{typeCondition}' and cannot be applied to selection type '{entity.Name}'.");
    }

    private static (MutationKind Kind, string EntityName) ParseOperation(string name)
    {
        foreach (var prefix in new[] { "create", "update", "delete", "upsert" })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                name.Length > prefix.Length)
            {
                var kind = prefix switch
                {
                    "create" => MutationKind.Create,
                    "update" => MutationKind.Update,
                    "delete" => MutationKind.Delete,
                    "upsert" => MutationKind.Upsert,
                    _ => throw new UnreachableException()
                };
                return (kind, name[prefix.Length..]);
            }
        }

        throw new InvalidOperationException(
            $"GraphQL mutation field '{name}' must use createX, updateX, deleteX, or upsertX naming.");
    }

    private SemanticEntity FindEntity(string name) =>
        _model.Entities.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"GraphQL mutation entity '{name}' is not defined in the semantic model.");

    private static bool NamesEqual(string schemaName, string graphqlName) =>
        string.Equals(schemaName, graphqlName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ToGraphQLName(schemaName), graphqlName, StringComparison.Ordinal);

    private static string ToGraphQLName(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private sealed record MutationBuildResult(
        IReadOnlyList<MutationFieldValue> Fields,
        SemanticFilterExpression? Filter,
        IReadOnlyList<ColumnId>? ConflictColumns,
        IReadOnlyList<NestedChildInput> NestedChildren);

    private sealed record NestedChildInput(
        SemanticRelationship Relationship,
        NestedMutationIntent Intent);
}