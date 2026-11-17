using System.Text.Json;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;

namespace Foundgine.SupplyChain.BenchmarkApi;

public static class GraphQLHttpEndpoint
{
    public static IEndpointRouteBuilder MapFoundgineGraphQL(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/graphql", HandleAsync);
        return endpoints;
    }

    private static readonly IReadOnlyDictionary<string, string[]> QueryRootRoles = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["salesOrder"] = ["sc-customer", "sc-operator", "sc-manager"],
        ["catalogProduct"] = ["sc-customer", "sc-operator", "sc-analyst", "sc-manager"],
        ["supplier"] = ["sc-analyst", "sc-manager"],
        ["shipment"] = ["sc-operator", "sc-manager"],
    };

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        FoundgineGraphQLQueryExecutor queries,
        FoundgineGraphQLMutationExecutor mutations,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        // The benchmark contract distinguishes invalid/missing bearer tokens (401)
        // from authenticated identities with no matching role/capability (GraphQL deny).
        if (http.User.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();

        GraphQLHttpRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<GraphQLHttpRequest>(
                http.Request.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                cancellationToken);
        }
        catch (JsonException)
        {
            return Results.Json(new { errors = new[] { new { message = "The GraphQL request body is not valid JSON.", extensions = new { code = "BAD_REQUEST" } } } });
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Query))
            return Results.Json(new { errors = new[] { new { message = "A GraphQL query is required.", extensions = new { code = "BAD_REQUEST" } } } });

        IReadOnlyDictionary<string, object?> variables;
        try
        {
            variables = ConvertVariables(request.Variables);
        }
        catch (JsonException)
        {
            return Results.Json(new { errors = new[] { new { message = "GraphQL variables must be a JSON object.", extensions = new { code = "BAD_USER_INPUT" } } } });
        }

        OperationType operation;
        FieldNode root;
        FieldNode[] rootFields;
        try
        {
            var document = Utf8GraphQLParser.Parse(request.Query);
            var operations = document.Definitions.OfType<OperationDefinitionNode>().ToArray();
            var selected = request.OperationName is null
                ? operations.Length == 1 ? operations[0] : throw new InvalidOperationException("An operationName is required when a document contains multiple operations.")
                : operations.FirstOrDefault(x => x.Name?.Value == request.OperationName)
                  ?? throw new InvalidOperationException($"GraphQL operation '{request.OperationName}' was not found.");
            operation = selected.Operation;
            rootFields = selected.SelectionSet.Selections.OfType<FieldNode>()
                .Where(x => !x.Name.Value.StartsWith("__", StringComparison.Ordinal)).ToArray();
            root = rootFields.FirstOrDefault()
                   ?? throw new InvalidOperationException("The GraphQL operation has no executable root field.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Error(GraphQLAdapterErrors.FromException(exception));
        }

        // Match the independent Hot Chocolate baseline's customer boundary. The
        // customer id is a validated Keycloak claim, never a GraphQL argument.
        var principal = http.User;
        if (principal.IsInRole("sc-customer"))
        {
            if (!int.TryParse(principal.FindFirst("customer_id")?.Value, out var customerId))
                return Error(new GraphQLAdapterError("FORBIDDEN", "A customer_id claim is required for customer operations.", Category: "FORBIDDEN"));

            if (operation == OperationType.Mutation)
            {
                // Mutation batches are supported by the adapter. Keep customer
                // ownership validation complete by allowing only one order-create root.
                if (rootFields.Length != 1 || root.Name.Value != "createSalesOrder")
                    return Error(new GraphQLAdapterError("FORBIDDEN", "Customers may only submit one createSalesOrder operation per request.", Category: "FORBIDDEN"));
                if (!variables.TryGetValue("input", out var inputValue) ||
                    inputValue is not IReadOnlyDictionary<string, object?> input ||
                    !input.TryGetValue("customerId", out var requestedCustomer) ||
                    !TryConvertInt32(requestedCustomer, out var requestedCustomerId) ||
                    requestedCustomerId != customerId)
                    return Error(new GraphQLAdapterError("FORBIDDEN", "Customers may only create orders for their own customer_id.", Category: "FORBIDDEN"));
            }
        }

        // Root-field authorization mirrors the Hot Chocolate baseline. Foundgine grants customers Shipment.read so that
        // their own orders can include shipments, but only SalesOrder has a row-level predicate, so a customer must not
        // be able to start a query at the Shipment root (that would expose every customer's shipments).
        if (operation == OperationType.Query)
        {
            foreach (var rootField in rootFields)
            {
                if (!QueryRootRoles.TryGetValue(rootField.Name.Value, out var allowedRoles) || !allowedRoles.Any(principal.IsInRole))
                    return Error(new GraphQLAdapterError("FORBIDDEN", $"The caller is not authorized to query '{rootField.Name.Value}'.", Category: "FORBIDDEN"));
            }
        }

        if (operation == OperationType.Mutation)
        {
            // TEMPORARY DIAGNOSTIC (remove after use): TryExecuteAsync converts every execution exception into a generic
            // EXECUTION_FAILED and logs nothing. In Development only, run the throwing overload first to print the real exception.
            if (http.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() == true)
            {
                try { await mutations.ExecuteAsync(request.Query, variables, request.OperationName, cancellationToken); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                { Console.Error.WriteLine("FOUNDGINE_DEBUG_MUTATION_EXCEPTION: " + exception); }
            }
            var result = await mutations.TryExecuteAsync(request.Query, variables, request.OperationName, cancellationToken);
            if (!result.Succeeded) return Error(result.Error!);
            return Results.Json(new { data = result.Data!.Data });
        }

        if (operation != OperationType.Query)
            return Error(new GraphQLAdapterError("GRAPHQL_VALIDATION_FAILED", "Only query and mutation operations are supported."));

        var queryResult = await queries.TryExecuteAsync(request.Query, variables, request.OperationName, cancellationToken);
        if (!queryResult.Succeeded) return Error(queryResult.Error!);

        // Reassemble the provider-neutral flat row/cell representation into the
        // nested GraphQL selection tree. Node ids are assigned preorder by the core
        // SemanticRequestResolver; relationship collections are de-duplicated by identity.
        var execution = queryResult.Data!.Execution;
        var shape = queryResult.Data.ResultShape;
        var rootEntity = model.Entities.FirstOrDefault(x =>
            string.Equals(x.Name, root.Name.Value, StringComparison.OrdinalIgnoreCase));
        if (rootEntity is null)
            return Error(new GraphQLAdapterError("GRAPHQL_ADAPTER_ERROR", "The GraphQL root entity is not present in the semantic model."));
        var projection = QueryProjection.Build(model, rootEntity.Id, shape);
        var projectedRows = QueryProjection.Materialize(execution.Rows, projection);
        var responseName = root.Alias?.Value ?? root.Name.Value;
        return Results.Json(new { data = new Dictionary<string, object?> { [responseName] = projectedRows } });
    }

    private static class QueryProjection
    {
        internal sealed record Child(GraphQLResultField Field, Node Node, bool IsMany);
        internal sealed record Node(int Id, EntityId Entity, FieldId IdentityField, GraphQLResultShape Shape, IReadOnlyList<Child> Children);

        internal sealed class Accumulator(Node node, object? key)
        {
            public Node Node { get; } = node;
            public object? Key { get; } = key;
            public Dictionary<string, object?> Scalars { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, Dictionary<string, Accumulator>> Many { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, Accumulator?> One { get; } = new(StringComparer.Ordinal);
        }

        public static Node Build(SemanticModel model, EntityId entity, GraphQLResultShape shape)
        {
            var nextId = 0;
            return BuildNode(model, entity, shape, ref nextId);
        }

        private static Node BuildNode(SemanticModel model, EntityId entity, GraphQLResultShape shape, ref int nextId)
        {
            var id = nextId++;
            var children = new List<Child>();
            var metadata = model.Get(entity);
            foreach (var field in shape.Fields.Where(x => x.Relationship is not null))
            {
                var relationship = metadata.Relationships.Single(x => x.Id == field.Relationship!.Value);
                var childShape = field.Children ?? new GraphQLResultShape([]);
                children.Add(new Child(field, BuildNode(model, relationship.Target, childShape, ref nextId),
                    relationship.Cardinality == RelationshipCardinality.Many));
            }
            return new Node(id, entity, metadata.Identity.FieldId, shape, children);
        }

        public static IReadOnlyList<object?> Materialize(IReadOnlyList<ExecutionRow> rows, Node root)
        {
            var roots = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
            var rowNumber = 0;
            foreach (var row in rows)
            {
                var key = ReadIdentity(row, root);
                var keyText = key is null ? "row:" + rowNumber : Key(key);
                if (!roots.TryGetValue(keyText, out var accumulator))
                    roots[keyText] = accumulator = new Accumulator(root, key);
                Merge(row, accumulator);
                rowNumber++;
            }
            return roots.Values.Select(ToValue).ToArray();
        }

        private static void Merge(ExecutionRow row, Accumulator accumulator)
        {
            var node = accumulator.Node;
            foreach (var field in node.Shape.Fields.Where(x => x.Field is not null))
            {
                var cell = new ExecutionCellKey(node.Id, node.Entity, field.Field!.Value);
                if (row.EffectiveCells.TryGetValue(cell, out var value))
                    accumulator.Scalars[field.Alias] = value;
            }

            foreach (var child in node.Children)
            {
                if (child.IsMany)
                {
                    if (!accumulator.Many.ContainsKey(child.Field.Alias))
                        accumulator.Many[child.Field.Alias] = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
                }
                else if (!accumulator.One.ContainsKey(child.Field.Alias))
                {
                    accumulator.One[child.Field.Alias] = null;
                }

                var key = ReadIdentity(row, child.Node);
                if (key is null) continue; // left-joined child absent
                var keyText = Key(key);
                Accumulator childAccumulator;
                if (child.IsMany)
                {
                    if (!accumulator.Many.TryGetValue(child.Field.Alias, out var collection))
                        accumulator.Many[child.Field.Alias] = collection = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
                    if (!collection.TryGetValue(keyText, out childAccumulator!))
                        collection[keyText] = childAccumulator = new Accumulator(child.Node, key);
                }
                else
                {
                    if (!accumulator.One.TryGetValue(child.Field.Alias, out var existing) || existing is null)
                        accumulator.One[child.Field.Alias] = childAccumulator = new Accumulator(child.Node, key);
                    else childAccumulator = existing;
                }
                Merge(row, childAccumulator);
            }
        }

        private static object? ReadIdentity(ExecutionRow row, Node node) =>
            row.EffectiveCells.TryGetValue(
                new ExecutionCellKey(node.Id, node.Entity, node.IdentityField), out var value)
                ? value
                : null;

        private static object ToValue(Accumulator accumulator)
        {
            var result = new Dictionary<string, object?>(accumulator.Scalars, StringComparer.Ordinal);
            foreach (var (name, values) in accumulator.Many)
                result[name] = values.Values.Select(ToValue).ToArray();
            foreach (var (name, value) in accumulator.One)
                result[name] = value is null ? null : ToValue(value);
            return result;
        }

        private static string Key(object value) => value is IFormattable formatted
            ? formatted.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? value.ToString()!
            : value.ToString()!;
    }

    private static bool TryConvertInt32(object? value, out int result)
    {
        var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        return int.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    private static IResult Error(GraphQLAdapterError error) => Results.Json(new
    {
        errors = new[] { new { message = error.Message, extensions = new { code = error.Code } } }
    });

    private static IReadOnlyDictionary<string, object?> ConvertVariables(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return new Dictionary<string, object?>();
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("Variables must be an object.");
        return element.EnumerateObject().ToDictionary(x => x.Name, x => ConvertValue(x.Value), StringComparer.Ordinal);
    }

    private static object? ConvertValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(x => x.Name, x => ConvertValue(x.Value), StringComparer.Ordinal),
        JsonValueKind.Array => value.EnumerateArray().Select(ConvertValue).ToArray(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new JsonException("Unsupported GraphQL variable value.")
    };

    public sealed class GraphQLHttpRequest
    {
        public string Query { get; set; } = "";
        public JsonElement Variables { get; set; }
        public string? OperationName { get; set; }
    }
}
