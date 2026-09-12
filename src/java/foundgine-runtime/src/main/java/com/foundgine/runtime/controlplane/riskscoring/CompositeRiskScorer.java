package com.foundgine.runtime.controlplane.riskscoring;

import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;

import java.util.List;
import java.util.Objects;

/**
 * Port of
 * {@code Foundgine.Runtime.ControlPlane.RiskScoring.CompositeRiskScorer},
 * declared alongside {@code IRiskRule} in the same C# file.
 *
 * <p>
 * Evaluates every registered {@link IRiskRule} and aggregates the resulting
 * signals into a single {@link RiskScore}.
 *
 * <p>
 * <b>Porting decision:</b> the C# constructor accepts a nullable
 * {@code IEnumerable<IRiskRule>?} defaulting to an empty sequence. Java has no
 * nullable-with-default parameter, so it is ported as two constructors: a
 * no-arg constructor equivalent to the C# default, and one taking an explicit
 * rule list.
 */
public final class CompositeRiskScorer {
	private final List<IRiskRule> rules;

	public CompositeRiskScorer() {
		this(List.of());
	}

	public CompositeRiskScorer(List<IRiskRule> rules) {
		this.rules = rules == null ? List.of() : List.copyOf(rules);
	}

	public RiskScore score(String toolName, SecurityExecutionContext security) {
		if (toolName == null || toolName.isBlank()) {
			throw new IllegalArgumentException("toolName is required.");
		}
		Objects.requireNonNull(security, "security");

		if (rules.isEmpty()) {
			return RiskScore.NONE;
		}

		var signals = rules.stream().map(rule -> rule.evaluate(toolName, security))
				.filter(signal -> signal.weight() > 0).toList();

		return RiskScore.aggregate(signals);
	}
}
