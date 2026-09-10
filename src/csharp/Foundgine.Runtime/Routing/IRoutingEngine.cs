namespace Foundgine.Runtime.Routing;

/// <summary>
/// A single routing concern (e.g. "high risk tools run isolated", "tools
/// tagged long-running go to background"). Rules abstain rather than
/// override, matching the abstention pattern used by
/// <c>ISemanticAuthorizationPolicy</c> and <c>IPolicyRule</c> elsewhere in
/// the control plane.
/// </summary>
public interface IRoutingRule
{
    /// <summary>Attempts to produce a contract for this context. Returns false to abstain.</summary>
    bool TryRoute(RoutingContext context, out TaskContract? contract);
}

/// <summary>
/// Decides <em>how</em> a tool call runs. The routing engine never selects a
/// backend provider — that remains an <c>IExecutionProvider</c>/DI concern
/// resolved by the host, unrelated to this decision.
/// </summary>
public interface IRoutingEngine
{
    TaskContract Route(RoutingContext context);
}