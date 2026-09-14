using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Query;
using HotChocolate.Language;

namespace Foundgine.Extensions.GraphQL.HotChocolate;

/// <summary>
/// Translates GraphQL query syntax into provider-neutral SemanticRequest
/// objects. GraphQL aliases, fragments, directives and variables are consumed
/// here and never leak into Foundgine core.
/// </summary>
public sealed class HotChocolateSemanticAdapter
{
    private readonly SemanticModel _model;

    public HotChocolateSemanticAdapter(SemanticModel model) =>
        _model = model ?? throw new ArgumentNullException(nameof(model));

    public SemanticRequest Adapt(string graphql) =>
        AdaptResultShape(graphql).Request;

    public SemanticRequest Adapt(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables,
        string? operationName = null) =>
        AdaptResultShape(graphql, variables, operationName).Request;

    public SemanticRequest Adapt(
        string graphql,
        string? operationName) =>
        AdaptResultShape(graphql, null, operationName).Request;

    public GraphQLQueryAdaptation AdaptResultShape(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? operationName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphql);

        var document = Utf8GraphQLParser.Parse(graphql);
        var operation = SelectOperation(document, OperationType.Query, operationName);
        var variableDefinitions = operation.VariableDefinitions
            .ToDictionary(x => x.Variable.Name.Value, x => x, StringComparer.Ordinal);

        GraphQLVariableCoercer.ValidateSuppliedVariables(variables, variableDefinitions);

        if (operation.Directives.Count != 0)
            throw new InvalidOperationException("GraphQL query operation directives are not supported by the adapter.");

        var roots = operation.SelectionSet.Selections
            .OfType<FieldNode>()
            .Where(x => !IsMetaField(x.Name.Value))
            .ToArray();

        if (roots.Length != 1)
            throw new InvalidOperationException(
                "Foundgine SemanticRequest supports exactly one root GraphQL field.");

        var rootField = roots[0];
        if (rootField.Arguments.Count == 0)
        {
            // no-op; the root field may legitimately have no arguments
        }

        var rootEntity = FindEntity(rootField.Name.Value);
        if (rootField.SelectionSet is null)
            throw new InvalidOperationException(
                $"Root GraphQL field '{rootField.Name.Value}' must select fields.");

        var fragments = document.Definitions
            .OfType<FragmentDefinitionNode>()
            .ToDictionary(x => x.Name.Value, x => x, StringComparer.Ordinal);

        var options = TranslateRootArguments(
            rootField, rootEntity, variables, variableDefinitions);

        var selection = TranslateSelectionSet(
            rootEntity,
            rootField.SelectionSet,
            fragments,
            variables,
            variableDefinitions,
            new HashSet<string>(StringComparer.Ordinal));

        var request = new SemanticRequest(rootEntity.Id, selection.Request, options);
        return new GraphQLQueryAdaptation(request, selection.Result);
    }

    public GraphQLAdapterResult<GraphQLQueryAdaptation> TryAdapt(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? operationName = null)
    {
        try
        {
            return GraphQLAdapterResult<GraphQLQueryAdaptation>.Success(
                AdaptResultShape(graphql, variables, operationName));
        }
        catch (Exception exception)
        {
            return GraphQLAdapterResult<GraphQLQueryAdaptation>.Failure(
                GraphQLAdapterErrors.FromException(exception));
        }
    }

    public SemanticRequest Adapt(SelectionSetNode selectionSet)
    {
        ArgumentNullException.ThrowIfNull(selectionSet);

        var rootFields = selectionSet.Selections
            .OfType<FieldNode>()
            .Where(x => !IsMetaField(x.Name.Value))
            .ToArray();

        if (rootFields.Length != 1)
            throw new InvalidOperationException(
                "Foundgine SemanticRequest supports exactly one root GraphQL field.");

        var rootField = rootFields[0];
        var rootEntity = FindEntity(rootField.Name.Value);
        if (rootField.SelectionSet is null)
            throw new InvalidOperationException(
                $"Root GraphQL field '{rootField.Name.Value}' must select fields.");

        var selection = TranslateSelectionSet(
            rootEntity,
            rootField.SelectionSet,
            new Dictionary<string, FragmentDefinitionNode>(),
            null,
            new Dictionary<string, VariableDefinitionNode>(),
            new HashSet<string>(StringComparer.Ordinal));

        return new SemanticRequest(
            rootEntity.Id,
            selection.Request,
            TranslateRootArguments(
                rootField,
                rootEntity,
                null,
                new Dictionary<string, VariableDefinitionNode>()));
    }

    private SemanticEntity FindEntity(string name) =>
        _model.Entities.FirstOrDefault(x => NamesEqual(x.Name, name))
        ?? throw new InvalidOperationException(
            $"GraphQL entity '{name}' is not defined in the semantic model.");

    private static OperationDefinitionNode SelectOperation(
        DocumentNode document,
        OperationType expectedType,
        string? operationName)
    {
        var operations = document.Definitions
            .OfType<OperationDefinitionNode>()
            .ToArray();

        if (operations.Length == 0)
            throw new InvalidOperationException(
                "GraphQL document contains no operation definitions.");

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
                "Selected GraphQL operation supports query operations only.");

        return operation;
    }

    private (IReadOnlyList<SemanticSelection> Request, GraphQLResultShape Result)
        TranslateSelectionSet(
            SemanticEntity entity,
            SelectionSetNode selectionSet,
            IReadOnlyDictionary<string, FragmentDefinitionNode> fragments,
            IReadOnlyDictionary<string, object?>? variables,
            IReadOnlyDictionary<string, VariableDefinitionNode> variableDefinitions,
            HashSet<string> fragmentStack)
    {
        var request = new List<SemanticSelection>();
        var resultFields = new List<GraphQLResultField>();
        var responseNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var selection in selectionSet.Selections)
        {
            switch (selection)
            {
                case FieldNode field:
                    if (!GraphQLDirectiveEvaluator.ShouldInclude(
                            field.Directives, variables, variableDefinitions))
                        continue;

                    if (IsMetaField(field.Name.Value))
                        continue;

                    ValidateFieldShape(field);
                    var translated = TranslateField(
                        entity,
                        field,
                        fragments,
                        variables,
                        variableDefinitions,
                        fragmentStack);

                    request.Add(translated.Request);
                    AddResultField(responseNames, resultFields, translated.Result);
                    break;

                case InlineFragmentNode inline:
                    if (!GraphQLDirectiveEvaluator.ShouldInclude(
                            inline.Directives, variables, variableDefinitions))
                        continue;

                    ValidateFragmentType(
                        entity,
                        inline.TypeCondition?.Name.Value,
                        "inline fragment");

                    if (inline.SelectionSet is null)
                        continue;

                    var inlineResult = TranslateSelectionSet(
                        entity,
                        inline.SelectionSet,
                        fragments,
                        variables,
                        variableDefinitions,
                        fragmentStack);

                    request.AddRange(inlineResult.Request);
                    foreach (var result in inlineResult.Result.Fields)
                        AddResultField(responseNames, resultFields, result);
                    break;

                case FragmentSpreadNode spread:
                    if (!GraphQLDirectiveEvaluator.ShouldInclude(
                            spread.Directives, variables, variableDefinitions))
                        continue;

                    if (!fragments.TryGetValue(spread.Name.Value, out var fragment))
                        throw new InvalidOperationException(
                            $"GraphQL fragment '{spread.Name.Value}' was not found.");

                    ValidateFragmentType(
                        entity,
                        fragment.TypeCondition.Name.Value,
                        $"fragment '{spread.Name.Value}'");

                    if (!fragmentStack.Add(spread.Name.Value))
                        throw new InvalidOperationException(
                            $"GraphQL fragment cycle detected at '{spread.Name.Value}'.");

                    var fragmentResult = TranslateSelectionSet(
                        entity,
                        fragment.SelectionSet,
                        fragments,
                        variables,
                        variableDefinitions,
                        fragmentStack);

                    fragmentStack.Remove(spread.Name.Value);

                    request.AddRange(fragmentResult.Request);
                    foreach (var result in fragmentResult.Result.Fields)
                        AddResultField(responseNames, resultFields, result);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"GraphQL selection '{selection.GetType().Name}' is not supported.");
            }
        }

        return (request, new GraphQLResultShape(resultFields));
    }

    private (
        SemanticSelection Request,
        GraphQLResultField Result)
        TranslateField(
            SemanticEntity entity,
            FieldNode field,
            IReadOnlyDictionary<string, FragmentDefinitionNode> fragments,
            IReadOnlyDictionary<string, object?>? variables,
            IReadOnlyDictionary<string, VariableDefinitionNode> variableDefinitions,
            HashSet<string> fragmentStack)
    {
        var semanticField = FindField(entity, field.Name.Value);

        if (semanticField is not null)
        {
            if (field.SelectionSet is not null)
                throw new InvalidOperationException(
                    $"Scalar GraphQL field '{field.Name.Value}' cannot contain a selection set.");

            return (
                new SemanticSelection(semanticField.Id, null, []),
                new GraphQLResultField(
                    semanticField.Id,
                    null,
                    field.Name.Value,
                    field.Alias?.Value ?? field.Name.Value));
        }

        var relationship = entity.Relationships.FirstOrDefault(r => NamesEqual(r.Name, field.Name.Value));

        if (relationship is null)
            throw new InvalidOperationException(
                $"GraphQL field '{field.Name.Value}' is not defined on semantic entity '{entity.Name}'.");

        if (field.SelectionSet is null)
            throw new InvalidOperationException(
                $"Relationship GraphQL field '{field.Name.Value}' must contain a selection set.");

        var target = _model.Get(relationship.Target);
        var children = TranslateSelectionSet(
            target,
            field.SelectionSet,
            fragments,
            variables,
            variableDefinitions,
            fragmentStack);

        return (
            new SemanticSelection(null, relationship.Id, children.Request),
            new GraphQLResultField(
                null,
                relationship.Id,
                field.Name.Value,
                field.Alias?.Value ?? field.Name.Value,
                children.Result));
    }

    private SemanticQueryOptions TranslateRootArguments(
        FieldNode rootField,
        SemanticEntity entity,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, VariableDefinitionNode> variableDefinitions)
    {
        SemanticFilterExpression? filter = null;
        var order = new List<SemanticOrderTerm>();
        int? limit = null;
        int? offset = null;
        string? after = null;

        foreach (var argument in rootField.Arguments)
        {
            var value = GraphQLVariableCoercer.ResolveValue(
                argument.Value, variables, variableDefinitions);

            switch (argument.Name.Value)
            {
                case "where":
                    if (filter is not null)
                        throw new InvalidOperationException(
                            "GraphQL root field may specify 'where' only once.");
                    filter = TranslateFilterValue(value, entity);
                    break;

                case "order":
                    order.AddRange(TranslateOrderValue(value, entity, []));
                    break;

                case "first":
                    limit = ReadInt(value, "first");
                    if (limit < 0)
                        throw new InvalidOperationException("'first' must be non-negative.");
                    break;

                case "skip":
                case "offset":
                    offset = ReadInt(value, argument.Name.Value);
                    if (offset < 0)
                        throw new InvalidOperationException(
                            $"'{argument.Name.Value}' must be non-negative.");
                    break;

                case "after":
                    if (value is not string cursor)
                        throw new InvalidOperationException(
                            "GraphQL argument 'after' must be a string cursor.");
                    after = cursor;
                    break;

                case "last":
                case "before":
                    throw new InvalidOperationException(
                        $"GraphQL argument '{argument.Name.Value}' is not supported by the forward keyset pagination contract.");

                default:
                    throw new InvalidOperationException(
                        $"GraphQL argument '{argument.Name.Value}' is not supported by the semantic query contract.");
            }
        }

        return new SemanticQueryOptions(filter, order, limit, offset, after);
    }

    private SemanticFilterExpression TranslateFilterValue(
        object? value,
        SemanticEntity entity)
    {
        if (value is not IReadOnlyDictionary<string, object?> dictionary)
            throw new InvalidOperationException("GraphQL 'where' must be an input object.");

        var expressions = new List<SemanticFilterExpression>();
        foreach (var pair in dictionary)
        {
            if (pair.Key.Equals("and", StringComparison.OrdinalIgnoreCase))
            {
                if (pair.Value is not System.Collections.IEnumerable list || pair.Value is string)
                    throw new InvalidOperationException("'and' must be a list of filter objects.");

                expressions.Add(new SemanticAndFilter(
                    list.Cast<object?>()
                        .Select(x => TranslateFilterValue(x, entity))
                        .ToArray()));
                continue;
            }

            if (pair.Key.Equals("or", StringComparison.OrdinalIgnoreCase))
            {
                if (pair.Value is not System.Collections.IEnumerable list || pair.Value is string)
                    throw new InvalidOperationException("'or' must be a list of filter objects.");

                expressions.Add(new SemanticOrFilter(
                    list.Cast<object?>()
                        .Select(x => TranslateFilterValue(x, entity))
                        .ToArray()));
                continue;
            }

            var relationship = entity.Relationships.FirstOrDefault(x => NamesEqual(x.Name, pair.Key));
            if (relationship is not null)
            {
                expressions.AddRange(
                    TranslateRelationshipFilterValue(pair.Value, relationship));
                continue;
            }

            var semanticField = FindField(entity, pair.Key)
                                ?? throw new InvalidOperationException(
                                    $"Filter field '{pair.Key}' is not defined on '{entity.Name}'.");

            if (pair.Value is IReadOnlyDictionary<string, object?> operators)
            {
                var fieldExpressions = operators.Select(op =>
                    new SemanticFieldFilter(
                        semanticField.Id,
                        ParseFilterOperator(op.Key),
                        op.Value is System.Collections.IEnumerable list &&
                        op.Value is not string &&
                        op.Key.Equals("in", StringComparison.OrdinalIgnoreCase)
                            ? list.Cast<object?>().ToArray()
                            : op.Value)).ToArray();

                expressions.AddRange(fieldExpressions);
            }
            else
            {
                expressions.Add(new SemanticFieldFilter(
                    semanticField.Id,
                    SemanticFilterOperator.Eq,
                    pair.Value));
            }
        }

        if (expressions.Count == 0)
            throw new InvalidOperationException("GraphQL 'where' cannot be empty.");

        return expressions.Count == 1
            ? expressions[0]
            : new SemanticAndFilter(expressions);
    }

    private IReadOnlyList<SemanticFilterExpression> TranslateRelationshipFilterValue(
        object? value,
        SemanticRelationship relationship)
    {
        if (relationship.Cardinality != RelationshipCardinality.Many)
            throw new InvalidOperationException(
                $"Relationship filter '{relationship.Name}' requires a collection relationship.");

        if (value is not IReadOnlyDictionary<string, object?> dictionary)
            throw new InvalidOperationException(
                $"Relationship filter '{relationship.Name}' must be an input object.");

        var target = _model.Get(relationship.Target);
        var expressions = new List<SemanticFilterExpression>();

        foreach (var aggregateField in dictionary)
        {
            if (aggregateField.Key.Equals("count", StringComparison.OrdinalIgnoreCase) ||
                aggregateField.Key.Equals("_count", StringComparison.OrdinalIgnoreCase))
            {
                expressions.AddRange(ParseAggregateObject(
                    aggregateField.Value,
                    relationship.Id,
                    SemanticFilterAggregate.Count,
                    null));
                continue;
            }

            if (aggregateField.Value is not IReadOnlyDictionary<string, object?> fieldAggregate)
                throw new InvalidOperationException(
                    $"Collection filter field '{aggregateField.Key}' must specify min or max.");

            var targetField = FindField(target, aggregateField.Key)
                              ?? throw new InvalidOperationException(
                                  $"Aggregate filter field '{aggregateField.Key}' is not defined on '{target.Name}'.");

            foreach (var aggregate in fieldAggregate)
            {
                var aggregateKind = aggregate.Key.ToLowerInvariant() switch
                {
                    "min" => SemanticFilterAggregate.Min,
                    "max" => SemanticFilterAggregate.Max,
                    _ => throw new InvalidOperationException(
                        $"Aggregate filter '{aggregate.Key}' is not supported.")
                };

                if (aggregate.Value is not IReadOnlyDictionary<string, object?> operators)
                    throw new InvalidOperationException(
                        $"Aggregate filter '{aggregate.Key}' for '{targetField.Name}' must be an input object.");

                foreach (var op in operators)
                {
                    expressions.Add(new SemanticAggregateFilter(
                        relationship.Id,
                        aggregateKind,
                        targetField.Id,
                        ParseAggregateOperator(op.Key),
                        op.Value));
                }
            }
        }

        return expressions;
    }

    private static IReadOnlyList<SemanticAggregateFilter> ParseAggregateObject(
        object? value,
        RelationshipId relationship,
        SemanticFilterAggregate aggregate,
        FieldId? field)
    {
        if (value is not IReadOnlyDictionary<string, object?> dictionary)
            throw new InvalidOperationException(
                "Aggregate filter must be an input object.");

        return dictionary.Select(op => new SemanticAggregateFilter(
            relationship,
            aggregate,
            field,
            ParseAggregateOperator(op.Key),
            op.Value)).ToArray();
    }

    private IReadOnlyList<SemanticOrderTerm> TranslateOrderValue(
        object? value,
        SemanticEntity entity,
        IReadOnlyList<RelationshipId> path)
    {
        if (value is not IReadOnlyDictionary<string, object?> dictionary)
            throw new InvalidOperationException(
                "GraphQL 'order' must be an input object.");

        var terms = new List<SemanticOrderTerm>();

        foreach (var pair in dictionary)
        {
            var semanticField = FindField(entity, pair.Key);
            if (semanticField is not null)
            {
                if (pair.Value is string direction)
                {
                    terms.Add(new SemanticOrderTerm(
                        semanticField.Id,
                        ParseSortDirection(direction),
                        path));
                    continue;
                }

                if (pair.Value is IReadOnlyDictionary<string, object?> aggregateObject &&
                    path.Count > 0)
                {
                    foreach (var aggregate in aggregateObject)
                    {
                        var kind = aggregate.Key.ToLowerInvariant() switch
                        {
                            "min" => SemanticOrderAggregate.Min,
                            "max" => SemanticOrderAggregate.Max,
                            _ => throw new InvalidOperationException(
                                $"Order aggregate '{aggregate.Key}' is not supported.")
                        };

                        if (aggregate.Value is not string aggregateDirection)
                            throw new InvalidOperationException(
                                $"Order aggregate '{aggregate.Key}' must use ASC or DESC.");

                        terms.Add(new SemanticOrderTerm(
                            semanticField.Id,
                            ParseSortDirection(aggregateDirection),
                            path,
                            kind));
                    }

                    continue;
                }

                throw new InvalidOperationException(
                    $"Order field '{pair.Key}' must use ASC/DESC or an aggregate object.");
            }

            var relationship = entity.Relationships.FirstOrDefault(r => NamesEqual(r.Name, pair.Key));

            if (relationship is null)
                throw new InvalidOperationException(
                    $"Order field '{pair.Key}' is not defined on '{entity.Name}'.");

            if (pair.Value is not IReadOnlyDictionary<string, object?> nested)
                throw new InvalidOperationException(
                    $"Order relationship '{pair.Key}' must contain nested order fields.");

            var nextPath = path.Append(relationship.Id).ToArray();
            var target = _model.Get(relationship.Target);

            if (relationship.Cardinality == RelationshipCardinality.Many)
            {
                foreach (var aggregateField in nested)
                {
                    if (aggregateField.Key.Equals("_count", StringComparison.OrdinalIgnoreCase))
                    {
                        if (aggregateField.Value is not string countDirection)
                            throw new InvalidOperationException(
                                "Collection _count order must use ASC or DESC.");

                        terms.Add(new SemanticOrderTerm(
                            target.Identity.FieldId,
                            ParseSortDirection(countDirection),
                            nextPath,
                            SemanticOrderAggregate.Count));
                        continue;
                    }

                    var targetField = FindField(target, aggregateField.Key)
                                      ?? throw new InvalidOperationException(
                                          $"Aggregate order field '{aggregateField.Key}' is not defined on '{target.Name}'.");

                    if (aggregateField.Value is not IReadOnlyDictionary<string, object?> aggregateObject)
                        throw new NotSupportedException(
                            $"Collection relationship order field '{aggregateField.Key}' must specify min or max.");

                    foreach (var aggregate in aggregateObject)
                    {
                        var kind = aggregate.Key.ToLowerInvariant() switch
                        {
                            "min" => SemanticOrderAggregate.Min,
                            "max" => SemanticOrderAggregate.Max,
                            _ => throw new InvalidOperationException(
                                $"Order aggregate '{aggregate.Key}' is not supported.")
                        };

                        if (aggregate.Value is not string direction)
                            throw new InvalidOperationException(
                                $"Order aggregate '{aggregate.Key}' must use ASC or DESC.");

                        terms.Add(new SemanticOrderTerm(
                            targetField.Id,
                            ParseSortDirection(direction),
                            nextPath,
                            kind));
                    }
                }

                continue;
            }

            terms.AddRange(TranslateOrderValue(nested, target, nextPath));
        }

        return terms;
    }

    private static SemanticField? FindField(SemanticEntity entity, string name)
    {
        var field = entity.Fields.FirstOrDefault(x => NamesEqual(x.Name, name));
        if (field is not null)
            return field;

        if (NamesEqual(entity.Identity.Name, name) ||
            string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
        {
            return new SemanticField(
                entity.Identity.FieldId,
                entity.Identity.Name,
                typeof(object));
        }

        return null;
    }

    private static SemanticFilterOperator ParseFilterOperator(string value) =>
        value.ToLowerInvariant() switch
        {
            "eq" => SemanticFilterOperator.Eq,
            "neq" => SemanticFilterOperator.Neq,
            "in" => SemanticFilterOperator.In,
            _ => throw new InvalidOperationException(
                $"Filter operator '{value}' is not supported.")
        };

    private static SemanticAggregateFilterOperator ParseAggregateOperator(string value) =>
        value.ToLowerInvariant() switch
        {
            "eq" => SemanticAggregateFilterOperator.Eq,
            "neq" => SemanticAggregateFilterOperator.Neq,
            "gt" => SemanticAggregateFilterOperator.Gt,
            "gte" => SemanticAggregateFilterOperator.Gte,
            "lt" => SemanticAggregateFilterOperator.Lt,
            "lte" => SemanticAggregateFilterOperator.Lte,
            _ => throw new InvalidOperationException(
                $"Aggregate filter operator '{value}' is not supported.")
        };

    private static SemanticSortDirection ParseSortDirection(string value) =>
        value.Equals("DESC", StringComparison.OrdinalIgnoreCase)
            ? SemanticSortDirection.Desc
            : value.Equals("ASC", StringComparison.OrdinalIgnoreCase)
                ? SemanticSortDirection.Asc
                : throw new InvalidOperationException(
                    $"Order direction '{value}' is not supported.");

    private static int ReadInt(object? value, string name) =>
        value switch
        {
            byte v => v,
            short v => v,
            int v => v,
            long v when v is >= int.MinValue and <= int.MaxValue => (int)v,
            uint v when v <= int.MaxValue => (int)v,
            ulong v when v <= int.MaxValue => (int)v,
            _ => throw new InvalidOperationException(
                $"GraphQL argument '{name}' must be an integer.")
        };

    private static void ValidateFieldShape(FieldNode field)
    {
        if (field.Arguments.Count != 0)
            throw new InvalidOperationException(
                $"GraphQL arguments on field '{field.Name.Value}' are not supported by the semantic adapter.");
    }

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

    private static void AddResultField(
        HashSet<string> responseNames,
        List<GraphQLResultField> fields,
        GraphQLResultField field)
    {
        if (!responseNames.Add(field.Alias))
        {
            var existing = fields.FirstOrDefault(x => x.Alias == field.Alias);
            if (existing is not null &&
                existing.Field == field.Field &&
                existing.Relationship == field.Relationship)
                return;

            throw new InvalidOperationException(
                $"GraphQL result contains duplicate response alias/name '{field.Alias}'.");
        }

        fields.Add(field);
    }

    private static bool IsMetaField(string name) =>
        name.StartsWith("__", StringComparison.Ordinal);

    private static bool NamesEqual(string schemaName, string graphqlName) =>
        string.Equals(schemaName, graphqlName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ToGraphQLName(schemaName), graphqlName, StringComparison.Ordinal);

    private static string ToGraphQLName(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}