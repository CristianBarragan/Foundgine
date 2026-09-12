package com.foundgine.core.security.penetration;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.*;
import com.foundgine.core.execution.security.*;
import com.foundgine.core.semantic.planning.*;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import com.foundgine.core.semantic.security.SecurityInvariantRegistry;
import org.junit.jupiter.api.Test;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

class PlanIntegrityPenetrationParityTest {
	private static ExecutionIR ir(String... invariants) {
		var node = new ExecutionIRNode(1, ExecutionOperation.SCAN, new EntityId(1), List.of(new FieldId(1)), null, null,
				List.of(), null, null, null);
		return new ExecutionIR(node, List.of(invariants), new SemanticPlanAuthorizationBinding("contract", "auth"));
	}

	private static class Plan extends ProviderPlan {
		Plan(String p) {
			super(p);
		}
	}

	private static class Good implements IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
			IProviderSecurityConformanceEvaluator {
		public java.util.Collection<String> preservedSecurityInvariants() {
			return SecurityInvariantRegistry.allInvariants().stream().map(x -> x.id()).toList();
		}

		public ProviderPlan compile(ExecutionIR ir) {
			return new Plan("pentest");
		}

		public ProviderSecurityConformanceResult evaluate(ExecutionIR ir, ProviderPlan plan) {
			return new ProviderSecurityConformanceResult(plan.provider(), ir.requiredSecurityInvariants(),
					ir.requiredSecurityInvariants(), List.of());
		}
	}

	private static class DeclarationOnly implements IProviderPlanCompiler, ISecurityInvariantProviderCompiler {
		public java.util.Collection<String> preservedSecurityInvariants() {
			return List.of(SecurityInvariantIds.TENANT_ISOLATION);
		}

		public ProviderPlan compile(ExecutionIR ir) {
			return new Plan("weak");
		}
	}

	@Test
	void missingSecurityProofCannotExecute() {
		assertThrows(IllegalStateException.class, () -> SecurityInvariantExecutionGate.ensureExecutable(new Plan("p"),
				ir(SecurityInvariantIds.AUTHORIZATION_REQUIRED)));
	}

	@Test
	void proofCannotBeAttachedToAnotherPlan() {
		var i = ir(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
		var p = new Plan("p");
		SecurityInvariantProofGate.attachAndValidate(p, i, new Good());
		assertThrows(IllegalStateException.class,
				() -> SecurityInvariantExecutionGate.ensureExecutable(new Plan("p"), i));
	}

	@Test
	void proofCannotAuthorizeModifiedIr() {
		var i = ir(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
		var p = new Plan("p");
		SecurityInvariantProofGate.attachAndValidate(p, i, new Good());
		assertThrows(IllegalStateException.class,
				() -> SecurityInvariantExecutionGate.ensureExecutable(p, ir(SecurityInvariantIds.TENANT_ISOLATION)));
	}

	@Test
	void providerMustConcretelyEvaluateCriticalInvariants() {
		assertThrows(IllegalStateException.class, () -> SecurityInvariantProofGate.attachAndValidate(new Plan("weak"),
				ir(SecurityInvariantIds.TENANT_ISOLATION), new DeclarationOnly()));
	}

	@Test
	void unknownInvariantCannotCrossCertificationBoundary() {
		assertThrows(IllegalStateException.class, () -> SecurityInvariantProofGate.attachAndValidate(new Plan("p"),
				ir("pentest.unknown-invariant"), new Good()));
	}
}
