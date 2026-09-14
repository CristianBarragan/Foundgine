using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Runtime.ControlPlane.RiskScoring;
using Foundgine.Runtime.ControlPlane.ToolRegistry;

namespace Foundgine.Runtime.ControlPlane.PolicyGateway;

/// <summary>
/// A single policy concern. Rules abstain (return null) rather than allow by
/// default, so silence never grants access — only an explicit
/// <see cref="PolicyDecision.Allow"/> from some rule, or the gateway's own
/// no-rules-registered default, does.
/// </summary>
public interface IPolicyRule
{
    PolicyDecision? Evaluate(ToolDescriptor tool, SecurityExecutionContext security, RiskScore riskScore);
}

public interface IPolicyGateway
{
    PolicyDecision Evaluate(ToolDescriptor tool, SecurityExecutionContext security, RiskScore riskScore);
}

/// <summary>
/// Evaluates every registered rule and resolves conflicts with a fixed
/// precedence: any <see cref="PolicyOutcome.Deny"/> wins outright; otherwise
/// any <see cref="PolicyOutcome.RequireApproval"/> wins; otherwise the call
/// is allowed only if at least one rule explicitly allowed it. An empty
/// rule set denies by default — governance with no configured policy must
/// not silently permit everything.
/// </summary>
public sealed class DefaultPolicyGateway : IPolicyGateway
{
    private readonly IReadOnlyList<IPolicyRule> _rules;

    public DefaultPolicyGateway(IEnumerable<IPolicyRule>? rules = null)
    {
        _rules = rules?.ToArray() ?? Array.Empty<IPolicyRule>();
    }

    public PolicyDecision Evaluate(ToolDescriptor tool, SecurityExecutionContext security, RiskScore riskScore)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(riskScore);

        var decisions = _rules
            .Select(rule => rule.Evaluate(tool, security, riskScore))
            .Where(d => d is not null)
            .Select(d => d!)
            .ToArray();

        var deny = decisions.FirstOrDefault(d => d.Outcome == PolicyOutcome.Deny);
        if (deny is not null)
            return deny;

        var requireApproval = decisions.FirstOrDefault(d => d.Outcome == PolicyOutcome.RequireApproval);
        if (requireApproval is not null)
            return requireApproval;

        var allow = decisions.FirstOrDefault(d => d.Outcome == PolicyOutcome.Allow);
        if (allow is not null)
            return allow;

        return PolicyDecision.Deny(
            policyId: "control-plane.default",
            reason: "No policy rule explicitly allowed this tool call.");
    }
}