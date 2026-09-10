package com.foundgine.runtime.controlplane.policygateway;

import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.runtime.controlplane.riskscoring.RiskScore;
import com.foundgine.runtime.controlplane.toolregistry.ToolDescriptor;

import java.util.List;
import java.util.Objects;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.PolicyGateway.DefaultPolicyGateway},
 * declared alongside {@code IPolicyGateway} in the same C# file.
 *
 * <p>Evaluates every registered rule and resolves conflicts with a fixed
 * precedence: any {@link PolicyDecision.PolicyOutcome#DENY} wins outright;
 * otherwise any {@link PolicyDecision.PolicyOutcome#REQUIRE_APPROVAL} wins;
 * otherwise the call is allowed only if at least one rule explicitly
 * allowed it. An empty rule set denies by default — governance with no
 * configured policy must not silently permit everything.
 *
 * <p><b>Porting decision:</b> the C# constructor accepts a nullable
 * {@code IEnumerable<IPolicyRule>?} defaulting to empty; ported as a no-arg
 * constructor plus one taking an explicit rule list, as elsewhere in this
 * module. {@code IPolicyRule.Evaluate} returning a nullable
 * {@code PolicyDecision?} (ported as {@code Optional<PolicyDecision>}) is
 * unwrapped here via {@code Optional::isPresent} / {@code Optional::get}
 * rather than a null filter.
 */
public final class DefaultPolicyGateway implements IPolicyGateway {
    private final List<IPolicyRule> rules;

    public DefaultPolicyGateway() {
        this(List.of());
    }

    public DefaultPolicyGateway(List<IPolicyRule> rules) {
        this.rules = rules == null ? List.of() : List.copyOf(rules);
    }

    @Override
    public PolicyDecision evaluate(ToolDescriptor tool, SecurityExecutionContext security, RiskScore riskScore) {
        Objects.requireNonNull(tool, "tool");
        Objects.requireNonNull(security, "security");
        Objects.requireNonNull(riskScore, "riskScore");

        var decisions = rules.stream()
                .map(rule -> rule.evaluate(tool, security, riskScore))
                .filter(java.util.Optional::isPresent)
                .map(java.util.Optional::get)
                .toList();

        for (var decision : decisions) {
            if (decision.outcome() == PolicyDecision.PolicyOutcome.DENY) {
                return decision;
            }
        }
        for (var decision : decisions) {
            if (decision.outcome() == PolicyDecision.PolicyOutcome.REQUIRE_APPROVAL) {
                return decision;
            }
        }
        for (var decision : decisions) {
            if (decision.outcome() == PolicyDecision.PolicyOutcome.ALLOW) {
                return decision;
            }
        }

        return PolicyDecision.deny(
                "control-plane.default",
                "No policy rule explicitly allowed this tool call.");
    }
}
