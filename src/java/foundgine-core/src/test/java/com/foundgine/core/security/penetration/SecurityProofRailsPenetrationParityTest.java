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

class SecurityProofRailsPenetrationParityTest {
	private static ExecutionIR ir(String... inv) {
		return new ExecutionIR(new ExecutionIRNode(1, ExecutionOperation.SCAN, new EntityId(1), List.of(new FieldId(1)),
				null, null, List.of(), null, null, null), List.of(inv), new SemanticPlanAuthorizationBinding("c", "a"));
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

		public ProviderPlan compile(ExecutionIR i) {
			return new Plan("p1");
		}

		public ProviderSecurityConformanceResult evaluate(ExecutionIR i, ProviderPlan p) {
			return new ProviderSecurityConformanceResult(p.provider(), i.requiredSecurityInvariants(),
					i.requiredSecurityInvariants(), List.of());
		}
	}

	private static class DeclarationOnly implements IProviderPlanCompiler, ISecurityInvariantProviderCompiler {
		public java.util.Collection<String> preservedSecurityInvariants() {
			return SecurityInvariantRegistry.allInvariants().stream().map(x -> x.id()).toList();
		}

		public ProviderPlan compile(ExecutionIR i) {
			return new Plan("weak");
		}
	}

	@Test
	void proofCannotBeReusedForDifferentPlanInstance() {
		var i = ir(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
		var p = new Plan("p1");
		SecurityInvariantProofGate.attachAndValidate(p, i, new Good());
		assertThrows(IllegalStateException.class,
				() -> SecurityInvariantExecutionGate.ensureExecutable(new Plan("p1"), i));
	}

	@Test
	void proofCannotBeReusedAfterProviderSubstitution() {
		var i = ir(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
		var p = new Plan("p1");
		SecurityInvariantProofGate.attachAndValidate(p, i, new Good());
		assertThrows(IllegalStateException.class,
				() -> SecurityInvariantExecutionGate.ensureExecutable(new Plan("attacker-provider"), i));
	}

	@Test
	void proofCannotBeReusedAfterSecurityObligationChange() {
		var i = ir(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
		var p = new Plan("p1");
		SecurityInvariantProofGate.attachAndValidate(p, i, new Good());
		assertThrows(IllegalStateException.class,
				() -> SecurityInvariantExecutionGate.ensureExecutable(p, ir(SecurityInvariantIds.TENANT_ISOLATION)));
	}

	@Test
	void providerDeclarationWithoutConcreteEvaluationCannotCertifyCriticalInvariant() {
		assertThrows(IllegalStateException.class, () -> SecurityInvariantProofGate.attachAndValidate(new Plan("weak"),
				ir(SecurityInvariantIds.RUNTIME_AUTHORIZATION), new DeclarationOnly()));
	}

	@Test
	void unknownInvariantCannotCrossCertificationBoundary() {
		assertThrows(IllegalStateException.class, () -> SecurityInvariantProofGate.attachAndValidate(new Plan("p1"),
				ir("security.attacker-controlled"), new Good()));
	}

	@Test
	void emptySecurityObligationsCannotProduceExecutablePlan() {
		assertThrows(IllegalStateException.class,
				() -> SecurityInvariantProofGate.attachAndValidate(new Plan("p1"), ir(), new Good()));
	}
}
