package com.foundgine.core.execution;

import java.util.Objects;

/** Final execution gate requiring a certificate for the exact plan and IR. */
public final class SecurityInvariantExecutionGate {
	private SecurityInvariantExecutionGate() {
	}

	public static void ensureExecutable(ProviderPlan plan, ExecutionIR ir) {
		Objects.requireNonNull(plan, "plan");
		Objects.requireNonNull(ir, "ir");
		var proof = plan.securityProof();
		if (proof == null)
			throw new IllegalStateException("Provider plan '" + plan.getClass().getSimpleName()
					+ "' has no security proof (certificate) and cannot execute.");
		if (!proof.provider().equals(plan.provider()))
			throw new IllegalStateException("Security certificate provider '" + proof.provider()
					+ "' does not match provider plan '" + plan.provider() + "'.");
		if (!proof.isBoundTo(plan, ir))
			throw new IllegalStateException(
					"Security certificate is not bound to the exact provider plan and Execution IR being executed."
							+ (proof.missing().isEmpty() ? ""
									: " Required security invariants not satisfied: "
											+ String.join(", ", proof.missing()) + "."));
		proof.ensureSatisfied();
	}
}
