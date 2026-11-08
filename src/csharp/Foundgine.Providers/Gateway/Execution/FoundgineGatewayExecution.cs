using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Providers.Gateway.Diagnostics;
using Foundgine.Runtime;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Gateway.Execution;

/// <summary>Secure adapter from gateway requests to Foundgine's canonical runtime interfaces.</summary>
public sealed class FoundgineGatewayExecution : IFoundgineGatewayExecution
{
    private readonly IFoundgineExecutor _executor;
    private readonly IFoundgineMutations _mutations;
    private readonly ISecurityExecutionContextProvider _security;
    private readonly IFoundgineGatewayEventPublisher _events;

    public FoundgineGatewayExecution(
        IFoundgineExecutor executor,
        IFoundgineMutations mutations,
        ISecurityExecutionContextProvider security,
        IFoundgineGatewayEventPublisher events)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _security = security ?? throw new ArgumentNullException(nameof(security));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public async Task<ExecutionResult> ExecuteReadAsync(
        ReadIntent intent,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var security = RequireSecurity("read execution");
        var correlationId = Guid.NewGuid().ToString("N");
        await PublishRequiredAsync(FoundgineGatewayEventType.ExecutionStarted, "semantic.read", correlationId, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            // Security material supplied in the request is never trusted; replace it with the host context.
            var securedIntent = intent with { Security = security };
            var result = await _executor.ExecuteAsync(securedIntent, context, cancellationToken).ConfigureAwait(false);
            await PublishNotificationAsync(FoundgineGatewayEventType.ExecutionCompleted, "semantic.read", correlationId, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            await PublishNotificationAsync(FoundgineGatewayEventType.AuthorizationDenied, "semantic.read", correlationId, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
        catch
        {
            await PublishNotificationAsync(FoundgineGatewayEventType.ExecutionFailed, "semantic.read", correlationId, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
    }

    public MutationDryRunResult DryRunMutation(SemanticMutationOperationGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var security = RequireSecurity("mutation dry-run");
        return _mutations.DryRun(new SemanticMutationRequest(graph, security));
    }

    public MutationPlanApproval ApproveMutation(SemanticMutationOperationGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var security = RequireSecurity("mutation approval");
        // The approver identity is taken from the host-established security subject, never the payload.
        return _mutations.Approve(new SemanticMutationRequest(graph, security), security.Subject);
    }

    public async Task<MutationExecutionResult> ExecuteMutationAsync(
        SemanticMutationOperationGraph graph,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var security = RequireSecurity("mutation execution");
        var correlationId = Guid.NewGuid().ToString("N");
        await PublishRequiredAsync(FoundgineGatewayEventType.ExecutionStarted, "semantic.mutation", correlationId, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var result = await _mutations.ExecuteAsync(
                new SemanticMutationRequest(graph, security), context, cancellationToken).ConfigureAwait(false);
            await PublishNotificationAsync(FoundgineGatewayEventType.ExecutionCompleted, "semantic.mutation", correlationId, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            await PublishNotificationAsync(FoundgineGatewayEventType.AuthorizationDenied, "semantic.mutation", correlationId, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
        catch
        {
            await PublishNotificationAsync(FoundgineGatewayEventType.ExecutionFailed, "semantic.mutation", correlationId, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
    }

    public async Task<MutationExecutionResult> ExecuteApprovedMutationAsync(
        MutationPlanApproval approval,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        var security = RequireSecurity("approved mutation execution");
        // Rebind to the current authenticated caller. The engine re-plans, re-authorizes,
        // and verifies the approved semantic plan fingerprint before provider execution.
        var securedApproval = approval with
        {
            Request = approval.Request with { Security = security }
        };
        var correlationId = Guid.NewGuid().ToString("N");
        await PublishRequiredAsync(FoundgineGatewayEventType.ExecutionStarted, "semantic.mutation.approved", correlationId, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var result = await _mutations.ExecuteApprovedAsync(securedApproval, context, cancellationToken)
                .ConfigureAwait(false);
            await PublishNotificationAsync(FoundgineGatewayEventType.ExecutionCompleted, "semantic.mutation.approved", correlationId, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            await PublishNotificationAsync(FoundgineGatewayEventType.AuthorizationDenied, "semantic.mutation.approved", correlationId, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
        catch
        {
            await PublishNotificationAsync(FoundgineGatewayEventType.ExecutionFailed, "semantic.mutation.approved", correlationId, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
    }

    private SecurityExecutionContext RequireSecurity(string operation) =>
        _security.RequireSecurityExecutionContext("Gateway", operation);

    private ValueTask PublishRequiredAsync(
        FoundgineGatewayEventType type,
        string subject,
        string correlationId,
        CancellationToken cancellationToken) =>
        _events.PublishAsync(type, subject, correlationId, cancellationToken: cancellationToken);

    // Completion notifications are not the durable audit record. A post-commit notification
    // failure must not make a committed operation look like a failed operation to its caller.
    private async ValueTask PublishNotificationAsync(
        FoundgineGatewayEventType type,
        string subject,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _events.PublishAsync(type, subject, correlationId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The operation has already returned or failed; do not mask its outcome with notification cancellation.
        }
        catch
        {
            FoundgineGatewayDiagnostics.RecordHookFailure("execution-notification", critical: false);
        }
    }
}
