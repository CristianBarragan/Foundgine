package com.foundgine.runtime.controlplane.policygateway;

import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.runtime.controlplane.riskscoring.RiskScore;
import com.foundgine.runtime.controlplane.toolregistry.ToolDescriptor;

import java.util.Optional;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.PolicyGateway.IPolicyRule}.
 *
 * <p>
 * A single policy concern. Rules abstain (return empty) rather than allow by
 * default, so silence never grants access — only an explicit
 * {@link PolicyDecision#allow} from some rule, or the gateway's own
 * no-rules-registered default, does.
 *
 * <p>
 * <b>Porting decision:</b> the C# method returns {@code PolicyDecision?} (a
 * nullable abstention). Ported as {@code Optional<PolicyDecision>} for the same
 * reason {@code IApprovalStore.TryGet} was: Java idiom prefers {@link Optional}
 * over a nullable return type to make abstention explicit at every call site.
 */
public interface IPolicyRule {
	Optional<PolicyDecision> evaluate(ToolDescriptor tool, SecurityExecutionContext security, RiskScore riskScore);
}
