using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Runtime;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Extensions.GraphQL.HotChocolate;

/// <summary>
/// Secure GraphQL mutation executor (Increment 9e-2), the write-side counterpart of
/// <see cref="FoundgineGraphQLQueryExecutor"/>. Pipeline, in order:
/// <list type="number">
///   <item>cancellation check;</item>
///   <item>host-provided <see cref="SecurityExecutionContext"/> is required and validated
///   (fail closed) <b>before</b> any GraphQL text is parsed, so an unauthenticated caller
///   can never trigger translation, planning or a write;</item>
///   <item>translation through <see cref="HotChocolateMutationAdapter"/> (batch form);</item>
///   <item>conversion to a semantic operation graph;</item>
///   <item>execution through <see cref="IFoundgineMutations"/> with the host context and the caller's token;</item>
///   <item>materialization and GraphQL result shaping.</item>
/// </list>
/// Tenant, subject, audience and warrant material come only from the host provider; GraphQL
/// input has no path to them. The context is read once per call and never cached.
/// </summary>
public sealed class FoundgineGraphQLMutationExecutor
{
    private const string Transport = "GraphQL";

    private readonly IFoundgineMutations _mutations;
    private readonly HotChocolateMutationAdapter _adapter;
    private readonly IMutationSchema _schema;
    private readonly ISecurityExecutionContextProvider _securityContextProvider;
    private readonly Func<ExecutionContext> _contextFactory;

    public FoundgineGraphQLMutationExecutor(
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

    /// <summary>
    /// Translates and executes a GraphQL mutation document. Throws
    /// <see cref="UnauthorizedAccessException"/> without a valid host security context,
    /// <see cref="OperationCanceledException"/> on cancellation, and otherwise lets
    /// translation/execution exceptions propagate. Prefer <see cref="TryExecuteAsync"/>.
    /// </summary>
    public async Task<GraphQLMutationExecutionResult> ExecuteAsync(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? operationName = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var security = RequireValidSecurityContext();
        var items = _adapter.AdaptBatchWithResultShape(graphql, variables, operationName);

        return await ExecuteItemsAsync(items, security, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Same as <see cref="ExecuteAsync"/> but reports failures as structured
    /// <see cref="GraphQLAdapterError"/>s: missing/invalid context → <c>UNAUTHENTICATED</c>;
    /// translation failure → <c>BAD_USER_INPUT</c> / <c>GRAPHQL_VALIDATION_FAILED</c> /
    /// <c>GRAPHQL_ADAPTER_ERROR</c>; authorization denial → <c>FORBIDDEN</c>; any other
    /// planning/execution/materialization failure → <c>EXECUTION_FAILED</c> with a generic
    /// message. Cancellation is never converted to an error.
    /// </summary>
    public async Task<GraphQLAdapterResult<GraphQLMutationExecutionResult>> TryExecuteAsync(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? operationName = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SecurityExecutionContext security;
        try
        {
            security = RequireValidSecurityContext();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var message = exception is UnauthorizedAccessException
                ? exception.Message
                : "GraphQL mutation execution requires a valid host-supplied SecurityExecutionContext.";
            return Failure(new GraphQLAdapterError(
                GraphQLAdapterErrorCode.Unauthenticated,
                message,
                Category: GraphQLAdapterErrorCode.Unauthenticated));
        }

        IReadOnlyList<GraphQLMutationBatchItem> items;
        try
        {
            items = _adapter.AdaptBatchWithResultShape(graphql, variables, operationName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failure(GraphQLAdapterErrors.FromException(exception));
        }

        try
        {
            var result = await ExecuteItemsAsync(items, security, cancellationToken).ConfigureAwait(false);
            return GraphQLAdapterResult<GraphQLMutationExecutionResult>.Success(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SemanticAuthorizationException
                                              or UnauthorizedAccessException)
        {
            return Failure(new GraphQLAdapterError(
                GraphQLAdapterErrorCode.Forbidden,
                "The caller is not authorized to perform this mutation.",
                Category: GraphQLAdapterErrorCode.Forbidden));
        }
        catch (Exception)
        {
            return Failure(new GraphQLAdapterError(
                GraphQLAdapterErrorCode.ExecutionFailed,
                "GraphQL mutation execution failed.",
                Category: "INTERNAL_SERVER_ERROR"));
        }
    }

    private async Task<GraphQLMutationExecutionResult> ExecuteItemsAsync(
        IReadOnlyList<GraphQLMutationBatchItem> items,
        SecurityExecutionContext security,
        CancellationToken cancellationToken)
    {
        var intents = items.Select(x => x.Adaptation.Intent).ToArray();
        var graph = GraphQLMutationSemanticConverter.ToSemanticGraph(intents, _schema);
        var request = new SemanticMutationRequest(graph, security);

        var execution = await _mutations
            .ExecuteAsync(request, _contextFactory(), cancellationToken)
            .ConfigureAwait(false);

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

    private SecurityExecutionContext RequireValidSecurityContext()
    {
        var security = _securityContextProvider.RequireSecurityExecutionContext(Transport, "mutation execution");

        if (security.Warrant is null
            || string.IsNullOrWhiteSpace(security.Subject)
            || string.IsNullOrWhiteSpace(security.Audience))
        {
            throw new UnauthorizedAccessException(
                "GraphQL mutation execution requires a SecurityExecutionContext with a warrant, subject and audience.");
        }

        return security;
    }

    private static GraphQLAdapterResult<GraphQLMutationExecutionResult> Failure(GraphQLAdapterError error) =>
        GraphQLAdapterResult<GraphQLMutationExecutionResult>.Failure(error);
}
