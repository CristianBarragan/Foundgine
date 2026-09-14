using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Runtime;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Extensions.GraphQL.HotChocolate;

/// <summary>
/// The outcome of a secured GraphQL query execution: the raw provider-neutral
/// execution result, plus the GraphQL result shape (aliases, nesting) needed to
/// project it into a GraphQL response. Foundgine does not ship response
/// materialization; the host maps <see cref="Execution"/> through
/// <see cref="ResultShape"/> into its own GraphQL response representation.
/// </summary>
public sealed record GraphQLQueryExecutionResult(
    ExecutionResult Execution,
    GraphQLResultShape ResultShape);

/// <summary>
/// Optional, secure-by-default execution entry point for GraphQL queries hosted
/// on Hot Chocolate. This is the query-side counterpart to
/// <c>Foundgine.Providers.Tools.MCP.FoundgineMcpTools</c>: it translates GraphQL text using
/// <see cref="HotChocolateSemanticAdapter"/>, requires a host-supplied
/// <see cref="SecurityExecutionContext"/> via <see cref="ISecurityExecutionContextProvider"/>,
/// and calls <see cref="IFoundgine"/> directly. GraphQL request payloads can
/// never supply identity, tenant, audience, or warrant material; that context
/// is always established by the host before this class is invoked.
///
/// Using this class is optional. <see cref="HotChocolateSemanticAdapter"/> remains
/// a pure GraphQL-to-<see cref="Foundgine.Core.Semantic.SemanticRequest"/> translator
/// with no security opinion, for hosts that want to wire execution themselves.
/// </summary>
public sealed class FoundgineHotChocolateQueryExecutor
{
    private readonly IFoundgine _foundgine;
    private readonly HotChocolateSemanticAdapter _adapter;
    private readonly ISecurityExecutionContextProvider _securityContextProvider;
    private readonly Func<ExecutionContext> _contextFactory;

    public FoundgineHotChocolateQueryExecutor(
        IFoundgine foundgine,
        HotChocolateSemanticAdapter adapter,
        ISecurityExecutionContextProvider securityContextProvider,
        Func<ExecutionContext>? contextFactory = null)
    {
        _foundgine = foundgine ?? throw new ArgumentNullException(nameof(foundgine));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _securityContextProvider = securityContextProvider
                                   ?? throw new ArgumentNullException(nameof(securityContextProvider));
        _contextFactory = contextFactory ?? (() => new ExecutionContext());
    }

    /// <summary>
    /// Translates and executes a GraphQL query. Throws <see cref="UnauthorizedAccessException"/>
    /// if the host has not established a <see cref="SecurityExecutionContext"/> for this call.
    /// GraphQL syntax/translation errors propagate as the same exceptions
    /// <see cref="HotChocolateSemanticAdapter.AdaptResultShape(string, IReadOnlyDictionary{string, object?}?, string?)"/>
    /// would throw directly. Prefer <see cref="TryExecuteAsync"/> when you want these
    /// mapped to stable GraphQL-facing error codes instead of thrown.
    /// </summary>
    public Task<GraphQLQueryExecutionResult> ExecuteAsync(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? operationName = null,
        CancellationToken cancellationToken = default)
    {
        // Security is established and required before translation runs, so a
        // GraphQL request that would fail translation never gets to imply
        // anything about what the caller was authorized to do.
        var security = _securityContextProvider.RequireSecurityExecutionContext(
            "GraphQL", "execution");

        var adaptation = _adapter.AdaptResultShape(graphql, variables, operationName);
        var request = adaptation.Request with { Security = security };

        return ExecuteAsyncCore(request, adaptation.Result, cancellationToken);
    }

    /// <summary>
    /// Same as <see cref="ExecuteAsync"/>, but maps translation, security, and execution
    /// failures into a stable <see cref="GraphQLAdapterError"/> via
    /// <see cref="GraphQLAdapterErrors.FromException"/> instead of throwing, so hosts can
    /// surface them as ordinary GraphQL response errors.
    /// </summary>
    public async Task<GraphQLAdapterResult<GraphQLQueryExecutionResult>> TryExecuteAsync(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? operationName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ExecuteAsync(graphql, variables, operationName, cancellationToken)
                .ConfigureAwait(false);
            return GraphQLAdapterResult<GraphQLQueryExecutionResult>.Success(result);
        }
        catch (Exception exception)
        {
            return GraphQLAdapterResult<GraphQLQueryExecutionResult>.Failure(
                GraphQLAdapterErrors.FromException(exception));
        }
    }

    private async Task<GraphQLQueryExecutionResult> ExecuteAsyncCore(
        Foundgine.Core.Semantic.SemanticRequest request,
        GraphQLResultShape resultShape,
        CancellationToken cancellationToken)
    {
        var execution = await _foundgine.ExecuteAsync(
            request,
            _contextFactory(),
            cancellationToken).ConfigureAwait(false);

        return new GraphQLQueryExecutionResult(execution, resultShape);
    }
}