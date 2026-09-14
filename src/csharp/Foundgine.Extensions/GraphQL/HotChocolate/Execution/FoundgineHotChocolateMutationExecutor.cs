using Foundgine.Runtime;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Security.Execution;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Extensions.GraphQL.HotChocolate;

/// <summary>
/// Secure GraphQL mutation execution boundary. GraphQL is an untrusted transport:
/// caller identity, tenant, audience and warrant data come only from the host-owned
/// security context provider and never from GraphQL input.
/// </summary>
public sealed class FoundgineHotChocolateMutationExecutor
{
    private readonly IFoundgineMutations _mutations;
    private readonly HotChocolateMutationAdapter _adapter;
    private readonly IMutationSchema _schema;
    private readonly ISecurityExecutionContextProvider _securityContextProvider;
    private readonly Func<ExecutionContext> _contextFactory;

    public FoundgineHotChocolateMutationExecutor(
        IFoundgineMutations mutations,
        HotChocolateMutationAdapter adapter,
        IMutationSchema schema,
        ISecurityExecutionContextProvider securityContextProvider,
        Func<ExecutionContext>? contextFactory = null)
    {
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _securityContextProvider = securityContextProvider
                                   ?? throw new ArgumentNullException(nameof(securityContextProvider));
        _contextFactory = contextFactory ?? (() => new ExecutionContext());
    }

    public async Task<GraphQLMutationExecutionResult> ExecuteAsync(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? operationName = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Establish trusted security before parsing/translating the request.
        // GraphQL variables and arguments can never supply or replace this context.
        var security = _securityContextProvider.RequireSecurityExecutionContext(
            "GraphQL", "mutation execution");

        var items = _adapter.AdaptBatchWithResultShape(graphql, variables, operationName);
        var intents = items.Select(x => x.Adaptation.Intent).ToArray();
        var graph = GraphQLMutationSemanticConverter.ToSemanticGraph(intents, _schema);
        var request = new SemanticMutationRequest(graph, security);

        var execution = await _mutations.ExecuteAsync(
            request, _contextFactory(), cancellationToken).ConfigureAwait(false);

        var materializer = new MutationResultMaterializer(_adapter.Model);
        var materialized = materializer.MaterializeBatch(
            items.Select(x => (x.ResultKey, x.Adaptation.Intent)).ToArray(), execution.Result);

        var byKey = items.ToDictionary(x => x.ResultKey, StringComparer.Ordinal);
        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var item in materialized)
        {
            var shape = byKey[item.Key].Adaptation.ResultShape;
            data[item.Key] = GraphQLMutationResultShaper.ShapeRoot(item.Result, shape);
        }

        return new GraphQLMutationExecutionResult(execution, data);
    }

    public async Task<GraphQLAdapterResult<GraphQLMutationExecutionResult>> TryExecuteAsync(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? operationName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return GraphQLAdapterResult<GraphQLMutationExecutionResult>.Success(
                await ExecuteAsync(graphql, variables, operationName, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            return GraphQLAdapterResult<GraphQLMutationExecutionResult>.Failure(
                GraphQLAdapterErrors.FromException(exception));
        }
    }
}

public sealed record GraphQLMutationExecutionResult(
    MutationExecutionResult Execution,
    IReadOnlyDictionary<string, object?> Data);