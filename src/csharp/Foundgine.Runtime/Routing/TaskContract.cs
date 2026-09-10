namespace Foundgine.Runtime.Routing;

/// <summary>Whether a routed task runs synchronously with the caller or is handed off.</summary>
public enum TaskExecutionMode
{
    /// <summary>Runs inline; the caller awaits the result directly.</summary>
    Foreground,

    /// <summary>Handed off to a host-owned worker; the caller receives a handle, not a result.</summary>
    Background,
}

/// <summary>Where a routed task is permitted to run.</summary>
public enum TaskRuntimeLocation
{
    /// <summary>Runs in the calling process.</summary>
    Local,

    /// <summary>Runs on a host-selected remote worker.</summary>
    Remote,

    /// <summary>Runs in a sandboxed/isolated execution boundary (e.g. no ambient network or file access).</summary>
    Isolated,
}

/// <summary>Whether a background task starts fresh or resumes prior worker state.</summary>
public enum TaskWorkerAssignment
{
    New,
    Resume,
}

/// <summary>
/// Retry behavior for a routed task. Foundgine only records the policy;
/// applying it is host-owned infrastructure, same as the runtime location.
/// </summary>
public sealed record RetryPolicy(int MaxAttempts, TimeSpan InitialBackoff, double BackoffMultiplier = 2.0)
{
    public static RetryPolicy None { get; } =
        new(MaxAttempts: 1, InitialBackoff: TimeSpan.Zero, BackoffMultiplier: 1.0);
}

/// <summary>Lifecycle characteristics a routed task's host-owned worker must honor.</summary>
public sealed record TaskLifecyclePolicy(bool Cancelable, bool Observable, RetryPolicy? Retry)
{
    public static TaskLifecyclePolicy Default { get; } =
        new(Cancelable: true, Observable: true, Retry: RetryPolicy.None);
}

/// <summary>
/// The output of a routing decision: how a tool call should run, not where
/// it executes. Foundgine records the contract; a host-owned runtime (worker
/// pool, sandbox, scheduler) is responsible for honoring it — the same
/// division used by <c>ControlPlane/Recovery</c> for authority infrastructure.
/// </summary>
public sealed record TaskContract(
    string TaskId,
    TaskExecutionMode Mode,
    TaskRuntimeLocation Runtime,
    TaskWorkerAssignment Worker,
    TaskLifecyclePolicy Lifecycle,
    IReadOnlyList<string> PolicyTags,
    string? ResumeWorkerId = null)
{
    /// <summary>The safe, no-special-handling default: run inline, locally, as a new unit of work.</summary>
    public static TaskContract Default(string taskId) => new(
        TaskId: taskId,
        Mode: TaskExecutionMode.Foreground,
        Runtime: TaskRuntimeLocation.Local,
        Worker: TaskWorkerAssignment.New,
        Lifecycle: TaskLifecyclePolicy.Default,
        PolicyTags: Array.Empty<string>());
}