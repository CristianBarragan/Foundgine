namespace Foundgine.Runtime.Routing;

/// <summary>
/// Evaluates <see cref="IRoutingRule"/>s in order and returns the first
/// non-abstaining result. Falls back to <see cref="TaskContract.Default"/>
/// (foreground, local, new) when no rule matches, so an empty or
/// misconfigured rule set never blocks execution — it just declines to
/// specialize it.
/// </summary>
public sealed class DefaultRoutingEngine : IRoutingEngine
{
    private readonly IReadOnlyList<IRoutingRule> _rules;

    public DefaultRoutingEngine(IEnumerable<IRoutingRule>? rules = null)
    {
        _rules = rules?.ToArray() ?? Array.Empty<IRoutingRule>();
    }

    public TaskContract Route(RoutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var rule in _rules)
        {
            if (rule.TryRoute(context, out var contract) && contract is not null)
                return contract;
        }

        return TaskContract.Default(taskId: Guid.NewGuid().ToString("n"));
    }
}