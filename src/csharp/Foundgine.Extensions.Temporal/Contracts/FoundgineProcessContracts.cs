using Foundgine.Extensions.Temporal.Activities;

namespace Foundgine.Extensions.Temporal.Contracts;

/// <summary>One step of a process: a named operation executed as a Temporal activity.</summary>
/// <param name="Name">Unique within the process; also the approval-gate and output key.</param>
/// <param name="Operation">Name of a registered <see cref="IFoundgineTemporalOperation"/>.</param>
/// <param name="CompensationOperation">Run (in reverse order) when a later step fails or the workflow is cancelled.</param>
/// <param name="RequiresApproval">Pause before the step until an approval signal arrives.</param>
/// <param name="ApprovalTimeout">Optional maximum duration to wait for approval. The workflow should follow its
/// configured timeout policy if approval is not received within this duration.</param>
public sealed record FoundgineProcessStep(
    string Name,
    string Operation,
    string? CompensationOperation = null,
    bool RequiresApproval = false,
    TimeSpan? ApprovalTimeout = null);

/// <summary>Server-side definition. Clients choose a process by name and never submit operations.</summary>
public sealed record FoundgineProcessDefinition(string Name, IReadOnlyList<FoundgineProcessStep> Steps);

/// <summary>Activity retry/timeout policy captured at start so workflow code stays deterministic across deploys.</summary>
public sealed record FoundgineActivityPolicy(
    TimeSpan StartToCloseTimeout, int MaximumAttempts, TimeSpan InitialInterval, TimeSpan MaximumInterval);

public sealed record FoundgineProcessInput(
    string TenantId,
    string Initiator,
    string BusinessKey,
    string PayloadJson,
    FoundgineProcessDefinition Definition,
    FoundgineActivityPolicy ActivityPolicy,
    string? CorrelationId = null,
    Guid? SourceMessageId = null);

public sealed record FoundgineProcessResult(
    string Outcome,
    IReadOnlyDictionary<string, string?> Outputs,
    string? Failure = null);

public sealed record FoundgineProcessStatus(
    string Process,
    string State,
    string? CurrentStep,
    IReadOnlyList<string> CompletedSteps,
    IReadOnlyList<string> CompensatedSteps,
    string? PendingApprovalStep,
    string? Failure);

public sealed record FoundgineApprovalDecision(string Step, bool Approved, string? Reason = null, string? DecidedBy = null);

public static class FoundgineProcessState
{
    public const string Running = "Running";
    public const string AwaitingApproval = "AwaitingApproval";
    public const string Compensating = "Compensating";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Rejected = "Rejected";
}

public sealed record FoundgineOperationRequest(
    string Operation,
    string TenantId,
    string Initiator,
    string? CorrelationId,
    string PayloadJson,
    IReadOnlyDictionary<string, string?> Outputs,
    bool Compensation);

public sealed record FoundgineOperationResult(string? OutputJson = null);
