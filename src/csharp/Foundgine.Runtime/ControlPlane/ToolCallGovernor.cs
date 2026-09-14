using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Runtime.ControlPlane.Approvals;
using Foundgine.Runtime.ControlPlane.AuditLog;
using Foundgine.Runtime.ControlPlane.PolicyGateway;
using Foundgine.Runtime.ControlPlane.RiskScoring;
using Foundgine.Runtime.ControlPlane.ToolRegistry;
using Foundgine.Runtime.Routing;

namespace Foundgine.Runtime.ControlPlane;

/// <summary>The result of governing a tool call: what happened, and — if permitted — how it should run.</summary>
public sealed record ToolCallGovernanceResult(
    PolicyOutcome Outcome,
    string Reason,
    TaskContract? Contract,
    ApprovalRequest? PendingApproval);

/// <summary>
/// Governs a single tool call end to end: looks the tool up in the
/// registry, scores its risk, evaluates policy, and either denies it, opens
/// a human approval request, or routes it to a <see cref="TaskContract"/> —
/// auditing every step along the way. This sits in front of
/// <c>Foundgine.Providers.Tools.MCP</c>; it does not call
/// <c>FoundgineEngine</c> or participate in the compile/authorize/execute
/// pipeline. An unknown or non-active tool is always denied.
/// </summary>
public sealed class ToolCallGovernor
{
    private readonly IToolRegistry _registry;
    private readonly CompositeRiskScorer _riskScorer;
    private readonly IPolicyGateway _policyGateway;
    private readonly IApprovalStore _approvals;
    private readonly IRoutingEngine _routing;
    private readonly IAuditLog _auditLog;

    public ToolCallGovernor(
        IToolRegistry registry,
        CompositeRiskScorer riskScorer,
        IPolicyGateway policyGateway,
        IApprovalStore approvals,
        IRoutingEngine routing,
        IAuditLog auditLog)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _riskScorer = riskScorer ?? throw new ArgumentNullException(nameof(riskScorer));
        _policyGateway = policyGateway ?? throw new ArgumentNullException(nameof(policyGateway));
        _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
    }

    public ToolCallGovernanceResult Govern(string toolName, SecurityExecutionContext security)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(security);

        if (!_registry.TryGet(toolName, out var tool) || tool is null || tool.Status != ToolStatus.Active)
        {
            var reason = $"Tool '{toolName}' is not registered or is not active.";
            Audit(AuditCategory.Denied, toolName, security, fingerprint: "unregistered", summary: reason);
            return new ToolCallGovernanceResult(PolicyOutcome.Deny, reason, Contract: null, PendingApproval: null);
        }

        var riskScore = _riskScorer.Score(toolName, security);
        Audit(AuditCategory.RiskScored, toolName, security, security.AuthorityCachePartition,
            $"Risk tier {riskScore.Tier} (value {riskScore.Value:F2}) from {riskScore.Signals.Count} signal(s).");

        var decision = _policyGateway.Evaluate(tool, security, riskScore);
        Audit(AuditCategory.PolicyEvaluated, toolName, security, security.AuthorityCachePartition,
            $"Policy '{decision.PolicyId}' -> {decision.Outcome}: {decision.Reason}");

        switch (decision.Outcome)
        {
            case PolicyOutcome.Deny:
                Audit(AuditCategory.Denied, toolName, security, security.AuthorityCachePartition, decision.Reason);
                return new ToolCallGovernanceResult(PolicyOutcome.Deny, decision.Reason, Contract: null,
                    PendingApproval: null);

            case PolicyOutcome.RequireApproval:
                var approval = _approvals.Create(security.AuthorityCachePartition);
                Audit(AuditCategory.ApprovalRequested, toolName, security, security.AuthorityCachePartition,
                    $"Approval '{approval.ApprovalId}' opened ({approval.RequiredApprovals} required).");
                return new ToolCallGovernanceResult(PolicyOutcome.RequireApproval, decision.Reason, Contract: null,
                    PendingApproval: approval);

            case PolicyOutcome.Allow:
                var routingContext = new RoutingContext(toolName, security, riskScore);
                var contract = _routing.Route(routingContext);
                Audit(AuditCategory.Routed, toolName, security, security.AuthorityCachePartition,
                    $"Routed as {contract.Mode}/{contract.Runtime}/{contract.Worker} (task '{contract.TaskId}').");
                return new ToolCallGovernanceResult(PolicyOutcome.Allow, decision.Reason, contract,
                    PendingApproval: null);

            default:
                throw new InvalidOperationException($"Unknown policy outcome '{decision.Outcome}'.");
        }
    }

    /// <summary>
    /// Resumes a call that was previously placed in <see cref="PolicyOutcome.RequireApproval"/>
    /// once its <see cref="ApprovalRequest"/> reaches <see cref="ApprovalStatus.Granted"/>. Routing
    /// only happens here, after a human has signed off — never during the initial request-approval step.
    /// </summary>
    public ToolCallGovernanceResult ResumeAfterApproval(string toolName, SecurityExecutionContext security,
        string approvalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(security);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);

        if (!_approvals.TryGet(approvalId, out var approval) || approval is null)
            throw new KeyNotFoundException($"No approval request '{approvalId}' exists.");

        Audit(AuditCategory.ApprovalDecided, toolName, security, security.AuthorityCachePartition,
            $"Approval '{approvalId}' is '{approval.Status}'.");

        if (approval.Status != ApprovalStatus.Granted)
        {
            var reason = $"Approval '{approvalId}' is '{approval.Status}', not granted.";
            return new ToolCallGovernanceResult(PolicyOutcome.Deny, reason, Contract: null, PendingApproval: approval);
        }

        var riskScore = _riskScorer.Score(toolName, security);
        var routingContext = new RoutingContext(toolName, security, riskScore);
        var contract = _routing.Route(routingContext);
        Audit(AuditCategory.Routed, toolName, security, security.AuthorityCachePartition,
            $"Routed after approval as {contract.Mode}/{contract.Runtime}/{contract.Worker} (task '{contract.TaskId}').");

        return new ToolCallGovernanceResult(PolicyOutcome.Allow, "Approved by human reviewer.", contract, approval);
    }

    private void Audit(AuditCategory category, string toolName, SecurityExecutionContext security, string fingerprint,
        string summary) =>
        _auditLog.Record(AuditEvent.Create(category, toolName, security.Subject, security.Tenant, fingerprint,
            summary));
}