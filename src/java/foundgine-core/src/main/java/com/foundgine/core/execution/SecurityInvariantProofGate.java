package com.foundgine.core.execution;

import com.foundgine.core.execution.security.IProviderSecurityConformanceEvaluator;
import com.foundgine.core.execution.security.ProviderSecurityConformanceResult;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import com.foundgine.core.semantic.security.SecurityInvariantRegistry;

import java.util.*;

/** Final certification boundary between provider compilation and execution. */
public final class SecurityInvariantProofGate {
	private static final Set<String> CONCRETE_EVALUATION_REQUIRED = Set.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED,
			SecurityInvariantIds.RUNTIME_AUTHORIZATION, SecurityInvariantIds.TENANT_ISOLATION,
			SecurityInvariantIds.FIELD_VISIBILITY, SecurityInvariantIds.RELATIONSHIP_VISIBILITY,
			SecurityInvariantIds.PARAMETERIZED_VALUES, SecurityInvariantIds.PLAN_CACHE_CONTEXT_ISOLATION,
			SecurityInvariantIds.ATOMIC_MUTATION, SecurityInvariantIds.MUTATION_ROW_LOCKING,
			SecurityInvariantIds.IDEMPOTENCY, SecurityInvariantIds.REPLAY_PROTECTION,
			SecurityInvariantIds.AUDIT_REQUIRED, SecurityInvariantIds.EXECUTION_EVIDENCE_REQUIRED);

	private SecurityInvariantProofGate() {
	}

	public static ProviderPlan attachAndValidate(ProviderPlan plan, ExecutionIR ir, IProviderPlanCompiler compiler) {
		Objects.requireNonNull(plan, "plan");
		Objects.requireNonNull(ir, "ir");
		Objects.requireNonNull(compiler, "compiler");

		var required = ir.requiredSecurityInvariants();
		if (required.isEmpty())
			throw new IllegalStateException(
					"ExecutionIR contains no security obligations. An executable provider plan must carry a non-empty security certificate.");

		if (!(compiler instanceof ISecurityInvariantProviderCompiler securityCompiler)) {
			throw new IllegalStateException("Provider compiler '" + compiler.getClass().getSimpleName()
					+ "' does not declare a security-invariant preservation contract.");
		}
		for (var id : required) {
			if (!SecurityInvariantRegistry.contains(id))
				throw new IllegalStateException("Unknown required security invariant '" + id + "'.");
		}

		var critical = required.stream().filter(CONCRETE_EVALUATION_REQUIRED::contains).toList();
		if (!critical.isEmpty() && !(compiler instanceof IProviderSecurityConformanceEvaluator)) {
			throw new IllegalStateException("Provider compiler '" + compiler.getClass().getSimpleName()
					+ "' has no concrete security conformance evaluator for security-critical invariants: "
					+ String.join(", ", critical) + ".");
		}

		Collection<String> preserved = securityCompiler.preservedSecurityInvariants();
		if (compiler instanceof IProviderSecurityConformanceEvaluator evaluator) {
			ProviderSecurityConformanceResult conformance = evaluator.evaluate(ir, plan);
			conformance.ensureSatisfied();
			var missing = required.stream().filter(x -> !conformance.satisfied().contains(x)).sorted().toList();
			if (!missing.isEmpty())
				throw new IllegalStateException("Provider '" + plan.provider()
						+ "' executable conformance did not satisfy required invariants: " + String.join(", ", missing)
						+ ".");
			preserved = conformance.satisfied();
		} else {
			final var declaredPreserved = preserved;
			var missing = required.stream().filter(x -> !declaredPreserved.contains(x)).sorted().toList();
			if (!missing.isEmpty())
				throw new IllegalStateException("Provider '" + plan.provider()
						+ "' declared preservation does not satisfy required invariants: " + String.join(", ", missing)
						+ ".");
		}

		// Bind the certificate to the exact returned ProviderPlan object.
		var proof = SecurityInvariantProof.create(plan, ir, required, preserved);
		proof.ensureSatisfied();
		plan.setSecurityProof(proof);
		return plan;
	}

}
