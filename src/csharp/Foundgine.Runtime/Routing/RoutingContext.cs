using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Runtime.ControlPlane.RiskScoring;

namespace Foundgine.Runtime.Routing;

/// <summary>
/// Inputs available to a routing decision. Routing decides <em>how</em> a
/// tool call runs (mode, runtime location, worker assignment, lifecycle),
/// never <em>which backend</em> executes it — backend selection remains a
/// host/DI concern resolved through <c>IExecutionProvider</c>.
/// </summary>
public sealed record RoutingContext(
    string ToolName,
    SecurityExecutionContext Security,
    RiskScore RiskScore,
    IReadOnlyDictionary<string, object?>? Hints = null)
{
    public IReadOnlyDictionary<string, object?> EffectiveHints =>
        Hints ?? EmptyHints;

    private static readonly IReadOnlyDictionary<string, object?> EmptyHints =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public bool TryGetHint(string key, out object? value) =>
        EffectiveHints.TryGetValue(key, out value);
}