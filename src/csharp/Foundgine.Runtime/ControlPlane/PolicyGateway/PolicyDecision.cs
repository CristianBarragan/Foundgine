namespace Foundgine.Runtime.ControlPlane.PolicyGateway;

/// <summary>The three outcomes a policy evaluation can produce for a tool call.</summary>
public enum PolicyOutcome
{
    Allow,
    RequireApproval,
    Deny,
}

/// <summary>
/// The result of evaluating policy for a tool call. Always carries the
/// policy that produced it and a human-readable reason — a bare
/// <see cref="PolicyOutcome"/> is never surfaced on its own, matching the
/// explainability requirement already established by
/// <see cref="Foundgine.Runtime.ControlPlane.RiskScoring.RiskSignal"/>.
/// </summary>
public sealed record PolicyDecision(
    PolicyOutcome Outcome,
    string PolicyId,
    string Reason,
    IReadOnlyList<string> ObligationTags)
{
    public static PolicyDecision Allow(string policyId, string reason) =>
        new(PolicyOutcome.Allow, policyId, reason, Array.Empty<string>());

    public static PolicyDecision Deny(string policyId, string reason) =>
        new(PolicyOutcome.Deny, policyId, reason, Array.Empty<string>());

    public static PolicyDecision RequireApproval(string policyId, string reason,
        IReadOnlyList<string>? obligationTags = null) =>
        new(PolicyOutcome.RequireApproval, policyId, reason, obligationTags ?? Array.Empty<string>());
}