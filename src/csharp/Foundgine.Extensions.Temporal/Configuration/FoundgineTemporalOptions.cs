using Foundgine.Extensions.Temporal.Contracts;

namespace Foundgine.Extensions.Temporal.Configuration;

/// <summary>Connection and execution defaults for the optional Temporal integration.</summary>
public sealed class FoundgineTemporalOptions
{
    public string Address { get; set; } = "localhost:7233";
    public string Namespace { get; set; } = "default";
    public string TaskQueue { get; set; } = "foundgine";

    /// <summary>Prefix of every workflow id. Ids are <c>{prefix}/{tenant}/{process}/{businessKey}</c>.</summary>
    public string WorkflowIdPrefix { get; set; } = "foundgine";

    /// <summary>Enables TLS (required by Temporal Cloud and any production cluster).</summary>
    public bool UseTls { get; set; }

    /// <summary>Temporal Cloud API key. Read it from a secret store; never commit it.</summary>
    public string? ApiKey { get; set; }

    public TimeSpan ActivityStartToCloseTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public int ActivityMaximumAttempts { get; set; } = 5;
    public TimeSpan ActivityInitialRetryInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan ActivityMaximumRetryInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Outbox topic → process name. Messages on unmapped topics follow <see cref="UnroutedTopicBehavior"/>.</summary>
    public Dictionary<string, string> TopicRoutes { get; } = new(StringComparer.Ordinal);

    public UnroutedTopicBehavior UnroutedTopicBehavior { get; set; } = UnroutedTopicBehavior.Ignore;

    internal FoundgineActivityPolicy ToActivityPolicy() => new(
        ActivityStartToCloseTimeout, ActivityMaximumAttempts, ActivityInitialRetryInterval, ActivityMaximumRetryInterval);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Address)) throw new ArgumentException("Address is required.", nameof(Address));
        if (string.IsNullOrWhiteSpace(Namespace)) throw new ArgumentException("Namespace is required.", nameof(Namespace));
        if (string.IsNullOrWhiteSpace(TaskQueue)) throw new ArgumentException("TaskQueue is required.", nameof(TaskQueue));
        if (string.IsNullOrWhiteSpace(WorkflowIdPrefix) || WorkflowIdPrefix.Contains('/'))
            throw new ArgumentException("WorkflowIdPrefix must be non-empty and contain no '/'.", nameof(WorkflowIdPrefix));
        if (ActivityStartToCloseTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ActivityStartToCloseTimeout));
        if (ActivityMaximumAttempts < 1) throw new ArgumentOutOfRangeException(nameof(ActivityMaximumAttempts));
        if (ActivityInitialRetryInterval <= TimeSpan.Zero || ActivityMaximumRetryInterval < ActivityInitialRetryInterval)
            throw new ArgumentOutOfRangeException(nameof(ActivityInitialRetryInterval));
    }
}

public enum UnroutedTopicBehavior
{
    /// <summary>Acknowledge and drop. Use when the outbox also feeds other consumers.</summary>
    Ignore,
    /// <summary>Throw so the outbox retries and eventually dead-letters the message.</summary>
    Fail
}
