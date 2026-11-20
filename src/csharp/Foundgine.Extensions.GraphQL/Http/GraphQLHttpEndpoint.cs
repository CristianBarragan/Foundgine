using System.Security.Claims;
using System.Text.Json;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic;
using Foundgine.Extensions.GraphQL.HotChocolate;
using HotChocolate.Language;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Foundgine.Extensions.GraphQL.Http;

/// <summary>What a host-supplied <see cref="GraphQLHttpEndpointOptions.Authorize"/> rule gets to look at, after parsing and before execution.</summary>
/// <param name="User">The authenticated principal (the endpoint already rejected anonymous callers with 401).</param>
/// <param name="Operation">Query or mutation (anything else is rejected by the endpoint).</param>
/// <param name="RootFields">Executable root fields of the selected operation, introspection fields excluded; never empty.</param>
/// <param name="Variables">Coerced request variables.</param>
public sealed record GraphQLHttpOperationContext(
    ClaimsPrincipal User,
    OperationType Operation,
    IReadOnlyList<FieldNode> RootFields,
    IReadOnlyDictionary<string, object?> Variables);

/// <summary>Configuration for <see cref="GraphQLHttpEndpoint.MapFoundgineGraphQL"/>.</summary>
public sealed class GraphQLHttpEndpointOptions
{
    /// <summary>Route of the POST endpoint. Default <c>/graphql</c>.</summary>
    public string Path { get; set; } = "/graphql";

    /// <summary>
    /// Query root field -> roles allowed to START a query there. Fail closed: a root field that is not listed is forbidden,
    /// so at least one entry is required. Use this to stop a role that holds a read capability only so related rows can be
    /// included (for example order lines under an order) from starting at that entity and reading every tenant's rows.
    /// </summary>
    public Dictionary<string, string[]> QueryRootRoles { get; } = new(StringComparer.Ordinal);

    /// <summary>Accept mutation operations (needs a registered <see cref="FoundgineGraphQLMutationExecutor"/>). Default false.</summary>
    public bool AllowMutations { get; set; }

    /// <summary>
    /// Optional host rule run after parsing and before any root-role check or execution (for example: a customer must carry a
    /// customer_id claim, or may only create orders for itself). Return an error to refuse the request, or null to continue.
    /// Claims must come from the validated token, never from a GraphQL argument.
    /// </summary>
    public Func<GraphQLHttpOperationContext, GraphQLAdapterError?>? Authorize { get; set; }
}

/// <summary>
/// HTTP front door for Foundgine GraphQL: bearer-authenticated POST, parse, host authorization hook, root-field role check,
/// then the secure Foundgine query/mutation executors, with the flat row/cell result re-assembled into the nested GraphQL shape.
/// Missing/invalid token -> HTTP 401; authenticated but not permitted -> a GraphQL error.
/// </summary>
public static class GraphQLHttpEndpoint
{
    public static IEndpointRouteBuilder MapFoundgineGraphQL(this IEndpointRouteBuilder endpoints, Action<GraphQLHttpEndpointOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new GraphQLHttpEndpointOptions();
        configure(options);
        if (options.QueryRootRoles.Count == 0)
            throw new InvalidOperationException("GraphQLHttpEndpointOptions.QueryRootRoles is empty: the endpoint fails closed, so list each query root field and the roles allowed to start there.");
        if (string.IsNullOrWhiteSpace(options.Path))
            throw new InvalidOperationException("GraphQLHttpEndpointOptions.Path must not be empty.");

        // Snapshot: later changes to the options object must not alter authorization of a mapped endpoint.
        var roots = new Dictionary<string, string[]>(options.QueryRootRoles, StringComparer.Ordinal);
        var allowMutations = options.AllowMutations;
        var authorize = options.Authorize;
        endpoints.MapPost(options.Path, (HttpContext http, CancellationToken cancellationToken) =>
            HandleAsync(http, roots, allowMutations, authorize, cancellationToken));
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        IReadOnlyDictionary<string, string[]> queryRootRoles,
        bool allowMutations,
        Func<GraphQLHttpOperationContext, GraphQLAdapterError?>? authorize,
        CancellationToken cancellationToken)
    {
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

        var principal = http.User;
        var denied = authorize?.Invoke(new GraphQLHttpOperationContext(principal, operation, rootFields, variables));
        if (denied is not null) return Error(denied);

        if (operation == OperationType.Query)
        {
            foreach (var rootField in rootFields)
            {
                if (!queryRootRoles.TryGetValue(rootField.Name.Value, out var allowedRoles) || !allowedRoles.Any(principal.IsInRole))
                    return Error(new GraphQLAdapterError("FORBIDDEN", $"The caller is not authorized to query '{rootField.Name.Value}'.", Category: "FORBIDDEN"));
            }
        }

        var services = http.RequestServices;
        if (operation == OperationType.Mutation && allowMutations)
        {
            var mutations = services.GetRequiredService<FoundgineGraphQLMutationExecutor>();
            var result = await mutations.TryExecuteAsync(request.Query, variables, request.OperationName, cancellationToken);
            if (!result.Succeeded) return Error(result.Error!);
            return Results.Json(new { data = result.Data!.Data });
        }

        if (operation != OperationType.Query)
            return Error(new GraphQLAdapterError("GRAPHQL_VALIDATION_FAILED",
                allowMutations ? "Only query and mutation operations are supported." : "Only query operations are supported."));

        var queries = services.GetRequiredService<FoundgineGraphQLQueryExecutor>();
        var model = services.GetRequiredService<SemanticModel>();
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

    internal sealed class GraphQLHttpRequest
    {
        public string Query { get; set; } = "";
        public JsonElement Variables { get; set; }
        public string? OperationName { get; set; }
    }
}
