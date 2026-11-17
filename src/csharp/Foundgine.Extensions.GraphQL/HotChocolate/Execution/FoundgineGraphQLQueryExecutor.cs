using Foundgine.Core.Execution;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Runtime;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Extensions.GraphQL.HotChocolate;

/// <summary>
/// Secure GraphQL query executor (Increment 9e-1). Pipeline, in order:
/// <list type="number">
///   <item>cancellation check;</item>
///   <item>host-provided <see cref="SecurityExecutionContext"/> is required and validated
///   (fail closed) <b>before</b> any GraphQL text is parsed;</item>
///   <item>translation through <see cref="HotChocolateSemanticAdapter"/>;</item>
///   <item>the host context replaces whatever <c>Security</c> the translation carried;</item>
///   <item>execution through <see cref="IFoundgineExecutor"/> with the caller's token.</item>
/// </list>
/// The security context is read from the provider once per call and never cached, so
/// one request's authority can never leak into another. GraphQL variables and query
/// text have no path to identity, tenant, audience or warrant material.
/// </summary>
public sealed class FoundgineGraphQLQueryExecutor
{
    private const string Transport = "GraphQL";

    private readonly IFoundgineExecutor _executor;
    private readonly HotChocolateSemanticAdapter _adapter;
    private readonly ISecurityExecutionContextProvider _securityContextProvider;
    private readonly Func<ExecutionContext> _contextFactory;

    public FoundgineGraphQLQueryExecutor(
        IFoundgineExecutor executor,
        HotChocolateSemanticAdapter adapter,
        ISecurityExecutionContextProvider securityContextProvider,
        Func<ExecutionContext>? contextFactory = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _securityContextProvider = securityContextProvider
                                   ?? throw new ArgumentNullException(nameof(securityContextProvider));
        _contextFactory = contextFactory ?? (() => new ExecutionContext());
    }

    /// <summary>
    /// Translates and executes a GraphQL query. Throws <see cref="UnauthorizedAccessException"/>
    /// when no valid security context is available, <see cref="OperationCanceledException"/>
    /// when <paramref name="cancellationToken"/> is cancelled, and otherwise lets translation
    /// and execution exceptions propagate. Prefer <see cref="TryExecuteAsync"/> for
    /// structured errors.
    /// </summary>
    public async Task<GraphQLQueryExecutionResult> ExecuteAsync(
        string graphql,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? operationName = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var security = RequireValidSecurityContext();

        var adaptation = _adapter.AdaptResultShape(graphql, variables, operationName);
        var request = adaptation.Request with { Security = security };

        var execution = await _executor
            .ExecuteAsync(request, _contextFactory(), cancellationToken)
            .ConfigureAwait(false);

        return new GraphQLQueryExecutionResult(execution, adaptation.Result);
    }

    /// <summary>
    /// Same as <see cref="ExecuteAsync"/> but reports failures as a structured
    /// <see cref="GraphQLAdapterError"/>:
    /// missing/invalid security context → <c>UNAUTHENTICATED</c>;
    /// translation failure → <c>BAD_USER_INPUT</c> / <c>GRAPHQL_VALIDATION_FAILED</c> /
    /// <c>GRAPHQL_ADAPTER_ERROR</c>;
    /// authorization denial during execution → <c>FORBIDDEN</c>;
    /// any other execution failure → <c>EXECUTION_FAILED</c> with a generic message
    /// (internal exception text is not exposed to the GraphQL client).
    /// Cancellation is never converted to an error; it propagates.
    /// </summary>
    public async Task<GraphQLAdapterResult<GraphQLQueryExecutionResult>> TryExecuteAsync(
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
            // Fail closed. Provider faults are reported as unauthenticated with a
            // generic message so host internals are not disclosed.
            var message = exception is UnauthorizedAccessException
                ? exception.Message
                : "GraphQL execution requires a valid host-supplied SecurityExecutionContext.";
            return Failure(new GraphQLAdapterError(
                GraphQLAdapterErrorCode.Unauthenticated,
                message,
                Category: GraphQLAdapterErrorCode.Unauthenticated));
        }

        GraphQLResultShape resultShape;
        SemanticRequest request;
        try
        {
            var adaptation = _adapter.AdaptResultShape(graphql, variables, operationName);
            request = adaptation.Request with { Security = security };
            resultShape = adaptation.Result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failure(GraphQLAdapterErrors.FromException(exception));
        }

        try
        {
            var execution = await _executor
                .ExecuteAsync(request, _contextFactory(), cancellationToken)
                .ConfigureAwait(false);
            return GraphQLAdapterResult<GraphQLQueryExecutionResult>.Success(
                new GraphQLQueryExecutionResult(execution, resultShape));
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
                "The caller is not authorized to perform this query.",
                Category: GraphQLAdapterErrorCode.Forbidden));
        }
        catch (Exception)
        {
            return Failure(new GraphQLAdapterError(
                GraphQLAdapterErrorCode.ExecutionFailed,
                "GraphQL query execution failed.",
                Category: "INTERNAL_SERVER_ERROR"));
        }
    }

    private SecurityExecutionContext RequireValidSecurityContext()
    {
        var security = _securityContextProvider.RequireSecurityExecutionContext(Transport, "execution");

        if (security.Warrant is null
            || string.IsNullOrWhiteSpace(security.Subject)
            || string.IsNullOrWhiteSpace(security.Audience))
        {
            throw new UnauthorizedAccessException(
                "GraphQL execution requires a SecurityExecutionContext with a warrant, subject and audience.");
        }

        return security;
    }

    private static GraphQLAdapterResult<GraphQLQueryExecutionResult> Failure(GraphQLAdapterError error) =>
        GraphQLAdapterResult<GraphQLQueryExecutionResult>.Failure(error);
}
