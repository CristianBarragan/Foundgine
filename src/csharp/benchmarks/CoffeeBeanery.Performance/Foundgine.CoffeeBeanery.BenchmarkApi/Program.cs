using Foundgine.CoffeeBeanery.BenchmarkApi;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql;
using Foundgine.Providers.Storage.Sql.Mutation.Postgres;
using Npgsql;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

var builder = WebApplication.CreateBuilder(args);
var connectionString =
    builder.Configuration.GetConnectionString("BankingConnectionString")
    ?? throw new InvalidOperationException(
        "Connection string 'BankingConnectionString' is not configured.");

var model = CoffeeBeanerySemanticModel.Build();
var metadata = CoffeeBeaneryMetadata.Build();
var policy = new AllowAllSemanticAuthorizationPolicy();
var contract = model.Freeze().CreateSnapshot();
var resolver = new SemanticRequestResolver(contract);
var authorizer = new SemanticAuthorizer(policy);
var planner = new Planner();
var compiler = new SqlCompiler(metadata);
var warmQueryCache = new MemoryProviderPlanCache();

var mutationAdapter = new HotChocolateMutationAdapter(model, metadata);
var mutationPlanner = new MutationPlanner(metadata);
var mutationAuthorizer = new MutationAuthorizer(metadata, policy);
var mutationMaterializer = new MutationResultMaterializer(model);

var app = builder.Build();

app.MapPost("/graphql/{mode}", async (
    string mode,
    GraphQLRequest request,
    CancellationToken cancellationToken) =>
{
    if (!string.Equals(mode, "cold", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(mode, "warm", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Mode must be 'cold' or 'warm'." });
    }

    try
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await SetSearchPathAsync(connection, cancellationToken);

        if (request.Query.TrimStart().StartsWith("mutation", StringComparison.OrdinalIgnoreCase))
        {
            // Always goes through the batch-capable adapter path now: a document with one
            // unaliased root field comes back as a single-item list (see
            // HotChocolateMutationAdapter.AdaptBatchWithResultShape), so this one path
            // handles both the plain single-mutation request and a real N-item batch
            // (multiple aliased root fields in one document) without a separate branch.
            var items = mutationAdapter.AdaptBatchWithResultShape(
                request.Query,
                request.Variables,
                request.OperationName);

            var plan = mutationPlanner.Plan(items.Select(i => i.Adaptation.Intent).ToArray());
            mutationAuthorizer.Authorize(plan);

            // Batches into ONE PostgreSQL statement via unnest() CTEs when the
            // batch shape allows it, and falls back to the sequential
            // SqlMutationCompiler/SqlMutationExecutionProvider path
            // automatically otherwise - the mutation never fails just because
            // it couldn't be batched.
            var result = new PostgresBatchedMutationExecutionProvider(connection, metadata)
                .ExecuteBatch(
                    ExecutionMutationIRCompiler.Compile(plan),
                    new ExecutionContext());

            var materializedItems = mutationMaterializer.MaterializeBatch(
                items.Select(i => (i.ResultKey, i.Adaptation.Intent)).ToArray(),
                result);

            var data = new Dictionary<string, object?>();
            foreach (var (key, materialized) in materializedItems)
            {
                var itemAdaptation = items.First(i => i.ResultKey == key).Adaptation;
                var root = materialized.Roots.FirstOrDefault();
                data[key] = root is null
                    ? null
                    : GraphQLMutationResultShaper.ShapeRoot(materialized, itemAdaptation.ResultShape);
            }

            return Results.Json(new Dictionary<string, object?> { ["data"] = data });
        }

        var queryAdaptation = new HotChocolateSemanticAdapter(model)
            .AdaptResultShape(request.Query, request.Variables, request.OperationName);
        var resolved = resolver.Resolve(queryAdaptation.Request);
        var semanticOperation = SemanticOperationCompiler.Compile(resolved);
        var authorization = authorizer.AuthorizeWithEvidence(contract, semanticOperation);
        var planQuery = planner.Plan(contract, authorization);

        var cache = string.Equals(mode, "warm", StringComparison.OrdinalIgnoreCase)
            ? (IProviderPlanCache)warmQueryCache
            : NoOpProviderPlanCache.Instance;

        var cacheKey = SemanticPlanFingerprint.CreateShapeKey(planQuery);
        var providerPlanQuery = cache.GetOrAdd(
            cacheKey,
            () => compiler.Compile(planQuery));

        var executionContext = BuildExecutionContext(planQuery);
        var execution = await new SqlExecutionProvider(connection)
            .ExecuteAsync(providerPlanQuery, executionContext, cancellationToken);

        return Results.Json(GraphQLResultShaper.Shape(
            queryAdaptation,
            model,
            execution,
            planQuery));
    }
    catch (Exception ex)
    {
        app.Logger.LogError(
            ex,
            "GraphQL request failed. Mode={Mode}, OperationName={OperationName}",
            mode,
            request.OperationName);

        return Results.Json(
            new
            {
                errors = new[]
                {
                    new
                    {
                        message = ex.Message
                    }
                }
            },
            statusCode: 400);
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (CancellationToken cancellationToken) =>
{
    try
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await SetSearchPathAsync(connection, cancellationToken);
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Database is not ready.",
            detail: ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});
app.Run();

static ExecutionContext BuildExecutionContext(Foundgine.Core.Semantic.Planning.SemanticPlan plan)
{
    var options = plan.Root.QueryOptions;
    if (options?.Limit is null && options?.Offset is null)
        return new ExecutionContext();

    var values = new Dictionary<string, object?>(StringComparer.Ordinal);
    if (options.Limit is { } limit)
        values[Foundgine.Core.Execution.ExecutionContextKeys.PaginationLimit] =
            limit + (options.After is not null ? 1 : 0);
    if (options.Offset is { } offset)
        values[Foundgine.Core.Execution.ExecutionContextKeys.PaginationOffset] = offset;
    values[Foundgine.Core.Execution.ExecutionContextKeys.PaginationHasCursor] = options.After is not null;

    return new ExecutionContext(values);
}

static async Task SetSearchPathAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SET search_path TO \"Banking\", \"Lending\", \"Accounting\";";
    await command.ExecuteNonQueryAsync(cancellationToken);
}

internal sealed record GraphQLRequest(
    string Query,
    Dictionary<string, object?>? Variables = null,
    string? OperationName = null);

internal sealed class NoOpProviderPlanCache : IProviderPlanCache
{
    internal static readonly NoOpProviderPlanCache Instance = new();

    public bool TryGet(string key, out ProviderPlan plan)
    {
        plan = null!;
        return false;
    }

    public void Set(string key, ProviderPlan plan)
    {
    }
}