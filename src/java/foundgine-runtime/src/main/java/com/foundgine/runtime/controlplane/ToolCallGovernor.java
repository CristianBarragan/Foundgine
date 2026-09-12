package com.foundgine.runtime.controlplane;

import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.runtime.controlplane.approvals.ApprovalRequest;
import com.foundgine.runtime.controlplane.approvals.IApprovalStore;
import com.foundgine.runtime.controlplane.auditlog.AuditEvent;
import com.foundgine.runtime.controlplane.auditlog.IAuditLog;
import com.foundgine.runtime.controlplane.policygateway.IPolicyGateway;
import com.foundgine.runtime.controlplane.policygateway.PolicyDecision;
import com.foundgine.runtime.controlplane.riskscoring.CompositeRiskScorer;
import com.foundgine.runtime.controlplane.toolregistry.IToolRegistry;
import com.foundgine.runtime.controlplane.toolregistry.ToolDescriptor;
import com.foundgine.runtime.routing.IRoutingEngine;
import com.foundgine.runtime.routing.RoutingContext;

import java.util.NoSuchElementException;
import java.util.Objects;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.ToolCallGovernor}.
 *
 * <p>Governs a single tool call end to end: looks the tool up in the
 * registry, scores its risk, evaluates policy, and either denies it, opens
 * a human approval request, or routes it to a {@code TaskContract} —
 * auditing every step along the way. This sits in front of
 * {@code Foundgine.Providers.Tools.MCP} (not yet ported); it does not call
 * {@code FoundgineEngine} or participate in the compile/authorize/execute
 * pipeline. An unknown or non-active tool is always denied.
 *
 * <p><b>Porting decision:</b> C#'s {@code $"..."} interpolated audit
 * summaries (e.g. {@code $"Risk tier {riskScore.Tier} (value {riskScore.Value:F2})..."})
 * are ported with {@link String#format} using {@code %.2f} for the C#
 * {@code F2} numeric format specifier.
 */
public final class ToolCallGovernor {
    private final IToolRegistry registry;
    private final CompositeRiskScorer riskScorer;
    private final IPolicyGateway policyGateway;
    private final IApprovalStore approvals;
    private final IRoutingEngine routing;
    private final IAuditLog auditLog;

    public ToolCallGovernor(
            IToolRegistry registry,
            CompositeRiskScorer riskScorer,
            IPolicyGateway policyGateway,
            IApprovalStore approvals,
            IRoutingEngine routing,
            IAuditLog auditLog) {
        this.registry = Objects.requireNonNull(registry, "registry");
        this.riskScorer = Objects.requireNonNull(riskScorer, "riskScorer");
        this.policyGateway = Objects.requireNonNull(policyGateway, "policyGateway");
        this.approvals = Objects.requireNonNull(approvals, "approvals");
        this.routing = Objects.requireNonNull(routing, "routing");
        this.auditLog = Objects.requireNonNull(auditLog, "auditLog");
    }

    public ToolCallGovernanceResult govern(String toolName, SecurityExecutionContext security) {
        if (toolName == null || toolName.isBlank()) {
            throw new IllegalArgumentException("toolName is required.");
        }
        Objects.requireNonNull(security, "security");

        var tool = registry.tryGet(toolName).orElse(null);
        if (tool == null || tool.status() != ToolDescriptor.ToolStatus.ACTIVE) {
            var reason = "Tool '" + toolName + "' is not registered or is not active.";
            audit(AuditEvent.AuditCategory.DENIED, toolName, security, "unregistered", reason);
            return new ToolCallGovernanceResult(PolicyDecision.PolicyOutcome.DENY, reason, null, null);
        }

        var riskScore = riskScorer.score(toolName, security);
        audit(AuditEvent.AuditCategory.RISK_SCORED, toolName, security, security.authorityCachePartition(),
                "Risk tier " + riskScore.tier() + " (value " + String.format("%.2f", riskScore.value())
                        + ") from " + riskScore.signals().size() + " signal(s).");

        var decision = policyGateway.evaluate(tool, security, riskScore);
        audit(AuditEvent.AuditCategory.POLICY_EVALUATED, toolName, security, security.authorityCachePartition(),
                "Policy '" + decision.policyId() + "' -> " + decision.outcome() + ": " + decision.reason());

        return switch (decision.outcome()) {
            case DENY -> {
                audit(AuditEvent.AuditCategory.DENIED, toolName, security, security.authorityCachePartition(),
                        decision.reason());
                yield new ToolCallGovernanceResult(PolicyDecision.PolicyOutcome.DENY, decision.reason(), null, null);
            }
            case REQUIRE_APPROVAL -> {
                var approval = approvals.create(security.authorityCachePartition());
                audit(AuditEvent.AuditCategory.APPROVAL_REQUESTED, toolName, security,
                        security.authorityCachePartition(),
                        "Approval '" + approval.approvalId() + "' opened (" + approval.requiredApprovals()
                                + " required).");
                yield new ToolCallGovernanceResult(PolicyDecision.PolicyOutcome.REQUIRE_APPROVAL, decision.reason(),
                        null, approval);
            }
            case ALLOW -> {
                var routingContext = new RoutingContext(toolName, security, riskScore, null);
                var contract = routing.route(routingContext);
                audit(AuditEvent.AuditCategory.ROUTED, toolName, security, security.authorityCachePartition(),
                        "Routed as " + contract.mode() + "/" + contract.runtime() + "/" + contract.worker()
                                + " (task '" + contract.taskId() + "').");
                yield new ToolCallGovernanceResult(PolicyDecision.PolicyOutcome.ALLOW, decision.reason(), contract,
                        null);
            }
        };
    }

    /**
     * Resumes a call that was previously placed in
     * {@link PolicyDecision.PolicyOutcome#REQUIRE_APPROVAL} once its
     * {@link ApprovalRequest} reaches {@link ApprovalRequest.ApprovalStatus#GRANTED}.
     * Routing only happens here, after a human has signed off — never during
     * the initial request-approval step.
     */
    public ToolCallGovernanceResult resumeAfterApproval(String toolName, SecurityExecutionContext security,
                                                          String approvalId) {
        if (toolName == null || toolName.isBlank()) {
            throw new IllegalArgumentException("toolName is required.");
        }
        Objects.requireNonNull(security, "security");
        if (approvalId == null || approvalId.isBlank()) {
            throw new IllegalArgumentException("approvalId is required.");
        }

        var approval = approvals.tryGet(approvalId)
                .orElseThrow(() -> new NoSuchElementException("No approval request '" + approvalId + "' exists."));

        audit(AuditEvent.AuditCategory.APPROVAL_DECIDED, toolName, security, security.authorityCachePartition(),
                "Approval '" + approvalId + "' is '" + approval.status() + "'.");

        if (approval.status() != ApprovalRequest.ApprovalStatus.GRANTED) {
            var reason = "Approval '" + approvalId + "' is '" + approval.status() + "', not granted.";
            return new ToolCallGovernanceResult(PolicyDecision.PolicyOutcome.DENY, reason, null, approval);
        }

        var riskScore = riskScorer.score(toolName, security);
        var routingContext = new RoutingContext(toolName, security, riskScore, null);
        var contract = routing.route(routingContext);
        audit(AuditEvent.AuditCategory.ROUTED, toolName, security, security.authorityCachePartition(),
                "Routed after approval as " + contract.mode() + "/" + contract.runtime() + "/" + contract.worker()
                        + " (task '" + contract.taskId() + "').");

        return new ToolCallGovernanceResult(PolicyDecision.PolicyOutcome.ALLOW, "Approved by human reviewer.",
                contract, approval);
    }

    private void audit(AuditEvent.AuditCategory category, String toolName, SecurityExecutionContext security,
                        String fingerprint, String summary) {
        auditLog.record(AuditEvent.create(category, toolName, security.subject(), security.tenant(), fingerprint,
                summary));
    }
}
