package com.foundgine.runtime.controlplane;

import com.foundgine.runtime.controlplane.approvals.IApprovalStore;
import com.foundgine.runtime.controlplane.approvals.InMemoryApprovalStore;
import com.foundgine.runtime.controlplane.auditlog.IAuditLog;
import com.foundgine.runtime.controlplane.auditlog.InMemoryAuditLog;
import com.foundgine.runtime.controlplane.policygateway.DefaultPolicyGateway;
import com.foundgine.runtime.controlplane.policygateway.IPolicyRule;
import com.foundgine.runtime.controlplane.riskscoring.CompositeRiskScorer;
import com.foundgine.runtime.controlplane.riskscoring.IRiskRule;
import com.foundgine.runtime.controlplane.toolregistry.InMemoryToolRegistry;
import com.foundgine.runtime.controlplane.toolregistry.ToolDescriptor;
import com.foundgine.runtime.routing.DefaultRoutingEngine;
import com.foundgine.runtime.routing.IRoutingRule;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.ToolGovernanceBuilder} and
 * {@code ToolGovernanceServiceCollectionExtensions.AddFoundgineToolGovernance},
 * merged into a single type.
 *
 * <p><b>Porting decision:</b> the C# source wires the governance pipeline
 * through {@code Microsoft.Extensions.DependencyInjection}'s
 * {@code IServiceCollection} (singleton registrations, with the last
 * registration winning so a host can override any default). This Java port
 * has no DI container dependency, so the same fluent shape is preserved but
 * retargeted at a plain builder that assembles the object graph directly:
 * {@link #addRiskRule}, {@link #addPolicyRule}, {@link #addRoutingRule}, and
 * {@link #registerTool} correspond to the C# builder's generic
 * {@code AddRiskRule<TRule>()} etc. (instantiated rule objects here, rather
 * than DI-resolved types), and {@link #approvalStore}/{@link #auditLog}
 * correspond to a host overriding the default {@code IApprovalStore}/
 * {@code IAuditLog} singleton registration. Calling {@link #build()}
 * corresponds to resolving {@code ToolCallGovernor} from the container.
 */
public final class ToolGovernanceBuilder {
    private final List<ToolDescriptor> tools = new ArrayList<>();
    private final List<IRiskRule> riskRules = new ArrayList<>();
    private final List<IPolicyRule> policyRules = new ArrayList<>();
    private final List<IRoutingRule> routingRules = new ArrayList<>();
    private IApprovalStore approvals = new InMemoryApprovalStore();
    private IAuditLog auditLog = new InMemoryAuditLog();

    public ToolGovernanceBuilder addRiskRule(IRiskRule rule) {
        riskRules.add(Objects.requireNonNull(rule, "rule"));
        return this;
    }

    public ToolGovernanceBuilder addPolicyRule(IPolicyRule rule) {
        policyRules.add(Objects.requireNonNull(rule, "rule"));
        return this;
    }

    public ToolGovernanceBuilder addRoutingRule(IRoutingRule rule) {
        routingRules.add(Objects.requireNonNull(rule, "rule"));
        return this;
    }

    /**
     * Seeds the tool registry with a descriptor at startup. Collected into
     * {@link InMemoryToolRegistry}'s constructor at {@link #build()} time,
     * so multiple calls accumulate rather than overwrite.
     */
    public ToolGovernanceBuilder registerTool(ToolDescriptor descriptor) {
        tools.add(Objects.requireNonNull(descriptor, "descriptor"));
        return this;
    }

    /** Overrides the default {@link InMemoryApprovalStore} with a durable implementation. */
    public ToolGovernanceBuilder approvalStore(IApprovalStore store) {
        this.approvals = Objects.requireNonNull(store, "store");
        return this;
    }

    /** Overrides the default {@link InMemoryAuditLog} with a durable implementation. */
    public ToolGovernanceBuilder auditLog(IAuditLog log) {
        this.auditLog = Objects.requireNonNull(log, "log");
        return this;
    }

    /** Assembles the governed tool-call pipeline from everything configured so far. */
    public ToolCallGovernor build() {
        var registry = new InMemoryToolRegistry(tools);
        var riskScorer = new CompositeRiskScorer(riskRules);
        var policyGateway = new DefaultPolicyGateway(policyRules);
        var routing = new DefaultRoutingEngine(routingRules);
        return new ToolCallGovernor(registry, riskScorer, policyGateway, approvals, routing, auditLog);
    }
}
