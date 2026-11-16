using Foundgine.Core.Semantic.Security.Execution;

namespace Foundgine.Extensions.Temporal.Security;

/// <summary>
/// Host-owned. Builds the trusted security context an activity runs under. Workflow history is persisted and
/// readable by operators, so warrants and tokens are never carried in workflow input: only tenant and initiator
/// identifiers are, and the worker re-derives authority here on every activity attempt.
/// </summary>
public interface IFoundgineTemporalSecurityContextFactory
{
    ValueTask<SecurityExecutionContext> CreateAsync(
        string tenantId, string initiator, CancellationToken cancellationToken = default);
}

/// <summary>
/// Per-activity holder exposed as <see cref="ISecurityExecutionContextProvider"/> so Foundgine services resolved
/// inside an activity (for example the gateway execution facade) see the activity's context, not an HTTP one.
/// </summary>
/// <remarks>
/// The runtime consumes a warrant's nonce once, so an activity that makes several Foundgine calls needs a fresh
/// warrant for each. The first call gets the context created up front; later calls get one minted by the same factory.
/// </remarks>
public sealed class FoundgineTemporalSecurityScope : ISecurityExecutionContextProvider
{
    private readonly object _gate = new();
    private SecurityExecutionContext? _first;
    private Func<SecurityExecutionContext>? _renew;

    public SecurityExecutionContext? GetSecurityExecutionContext()
    {
        lock (_gate)
        {
            if (_first is { } first) { _first = null; return first; }
            return _renew?.Invoke();
        }
    }

    internal void Set(SecurityExecutionContext context, Func<SecurityExecutionContext>? renew = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_gate) { _first = context; _renew = renew; }
    }
}
