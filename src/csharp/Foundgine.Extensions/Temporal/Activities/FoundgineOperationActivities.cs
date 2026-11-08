using Foundgine.Extensions.Temporal.Contracts;
using Foundgine.Extensions.Temporal.Observability;
using Foundgine.Extensions.Temporal.Security;
using Temporalio.Activities;
using Temporalio.Exceptions;

namespace Foundgine.Extensions.Temporal.Activities;

/// <summary>Context given to an operation. The idempotency key is stable across retries of the same activity.</summary>
public sealed record FoundgineOperationContext(
    string TenantId,
    string Initiator,
    string? CorrelationId,
    string PayloadJson,
    IReadOnlyDictionary<string, string?> Outputs,
    string IdempotencyKey,
    bool IsCompensation,
    int Attempt);

/// <summary>
/// A unit of business work. Implementations are resolved from the activity scope, so they can inject
/// IFoundgineGatewayExecution / IFoundgine and run through Foundgine's authorization boundary. They MUST be
/// idempotent: Temporal delivers activities at-least-once.
/// </summary>
public interface IFoundgineTemporalOperation
{
    string Name { get; }
    Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext context, CancellationToken cancellationToken);
}

/// <summary>Throw to fail the step without Temporal retrying it (validation failure, business rule, denial).</summary>
public sealed class FoundgineNonRetryableOperationException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class FoundgineOperationActivities(
    IEnumerable<IFoundgineTemporalOperation> operations,
    IFoundgineTemporalSecurityContextFactory securityFactory,
    FoundgineTemporalSecurityScope securityScope)
{
    public const string ActivityName = "foundgine.operation.execute";

    [Activity(ActivityName)]
    public async Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationRequest request)
    {
        var activity = ActivityExecutionContext.Current;
        var info = activity.Info;
        var ct = activity.CancellationToken;

        var operation = operations.FirstOrDefault(o => string.Equals(o.Name, request.Operation, StringComparison.Ordinal))
            ?? throw new ApplicationFailureException(
                $"No Foundgine operation named '{request.Operation}' is registered on this worker.",
                errorType: "OperationNotRegistered", nonRetryable: true);

        using var span = FoundgineTemporalDiagnostics.StartOperation(request.Operation, request.Compensation);
        try
        {
            var first = await securityFactory.CreateAsync(request.TenantId, request.Initiator, ct).ConfigureAwait(false);
            // Each Foundgine call inside the operation needs its own single-use warrant (see FoundgineTemporalSecurityScope).
            securityScope.Set(first, () => securityFactory.CreateAsync(request.TenantId, request.Initiator, ct)
                .AsTask().GetAwaiter().GetResult());

            var context = new FoundgineOperationContext(
                request.TenantId, request.Initiator, request.CorrelationId, request.PayloadJson, request.Outputs,
                IdempotencyKey: $"{info.WorkflowId}:{info.ActivityId}",
                IsCompensation: request.Compensation, Attempt: info.Attempt);

            return await operation.ExecuteAsync(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (FoundgineNonRetryableOperationException ex)
        {
            throw new ApplicationFailureException(ex.Message, ex, errorType: "OperationRejected", nonRetryable: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Authorization failures are deterministic; retrying cannot fix them.
            throw new ApplicationFailureException(ex.Message, ex, errorType: "Unauthorized", nonRetryable: true);
        }
    }
}
