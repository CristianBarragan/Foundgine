using Foundgine.Core.Semantic.Security.Execution;

namespace Foundgine.Runtime.ControlPlane.RiskScoring;

/// <summary>
/// A single, independently-testable risk factor (e.g. "tool is tagged
/// destructive", "caller has no prior successful calls this session").
/// Unlike <c>IRoutingRule</c> and <c>IPolicyRule</c>, risk rules don't
/// abstain — every rule contributes a signal (possibly zero-weight), so
/// scoring is a pure sum, not a resolution order.
/// </summary>
public interface IRiskRule
{
    RiskSignal Evaluate(string toolName, SecurityExecutionContext security);
}

/// <summary>
/// Evaluates every registered <see cref="IRiskRule"/> and aggregates the
/// resulting signals into a single <see cref="RiskScore"/>.
/// </summary>
public sealed class CompositeRiskScorer
{
    private readonly IReadOnlyList<IRiskRule> _rules;

    public CompositeRiskScorer(IEnumerable<IRiskRule>? rules = null)
    {
        _rules = rules?.ToArray() ?? Array.Empty<IRiskRule>();
    }

    public RiskScore Score(string toolName, SecurityExecutionContext security)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(security);

        if (_rules.Count == 0)
            return RiskScore.None;

        var signals = _rules
            .Select(rule => rule.Evaluate(toolName, security))
            .Where(signal => signal.Weight > 0)
            .ToArray();

        return RiskScore.Aggregate(signals);
    }
}