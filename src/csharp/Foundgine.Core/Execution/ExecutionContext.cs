namespace Foundgine.Core.Execution;

/// <summary>Runtime values supplied to an already-planned execution.
/// Semantic planning remains independent of these values. A deadline is an
/// execution-time security boundary and is never part of semantic plan shape.</summary>
public sealed record ExecutionContext(
    IReadOnlyDictionary<string, object?>? Values = null)
{
    public IReadOnlyDictionary<string, object?> EffectiveValues => Values ?? EmptyValues;

    /// <summary>Absolute UTC deadline after which execution must not commit or complete successfully.</summary>
    public DateTimeOffset? DeadlineUtc { get; init; }

    private static readonly IReadOnlyDictionary<string, object?> EmptyValues =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public bool TryGetValue(string path, out object? value) =>
        EffectiveValues.TryGetValue(path, out value);

    public void EnsureWithinDeadline(DateTimeOffset? now = null)
    {
        if (DeadlineUtc is { } deadline && (now ?? DateTimeOffset.UtcNow) >= deadline)
            throw new TimeoutException($"Execution deadline '{deadline:O}' has expired.");
    }

    public CancellationTokenSource CreateDeadlineCancellationSource(CancellationToken callerToken)
    {
        EnsureWithinDeadline();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        if (DeadlineUtc is { } deadline)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                cts.Cancel();
            }
            else
            {
                cts.CancelAfter(remaining);
            }
        }

        return cts;
    }
}