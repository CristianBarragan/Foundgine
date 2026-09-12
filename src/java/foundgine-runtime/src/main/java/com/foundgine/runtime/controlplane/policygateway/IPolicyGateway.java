package com.foundgine.runtime.controlplane.policygateway;

import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.runtime.controlplane.riskscoring.RiskScore;
import com.foundgine.runtime.controlplane.toolregistry.ToolDescriptor;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.PolicyGateway.IPolicyGateway}.
 */
public interface IPolicyGateway {
	PolicyDecision evaluate(ToolDescriptor tool, SecurityExecutionContext security, RiskScore riskScore);
}
