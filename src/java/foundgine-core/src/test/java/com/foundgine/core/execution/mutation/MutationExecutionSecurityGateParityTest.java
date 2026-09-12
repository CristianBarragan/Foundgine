package com.foundgine.core.execution.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.planning.mutation.*;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/** Mirrors the C# mutation execution certificate trust-boundary tests. */
class MutationExecutionSecurityGateParityTest {
	@Test
	void certificateIsBoundToExactMutationIrAndProviderInstance() {
		var ir = testIr(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
		var provider = new HonestProvider();
		var certificate = MutationExecutionSecurityGate.certify(ir, provider, provider.getClass().getName(),
				List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED));
		MutationExecutionSecurityGate.ensureExecutable(ir, provider, certificate);

		var cloned = new ExecutionMutationIR(ir.operations(), ir.dependencies(), ir.requiredSecurityInvariants());
		var irEx = assertThrows(IllegalStateException.class,
				() -> MutationExecutionSecurityGate.ensureExecutable(cloned, provider, certificate));
		assertTrue(irEx.getMessage().toLowerCase().contains("exact mutation ir"));

		var providerEx = assertThrows(IllegalStateException.class,
				() -> MutationExecutionSecurityGate.ensureExecutable(ir, new HonestProvider(), certificate));
		assertTrue(providerEx.getMessage().toLowerCase().contains("exact mutation ir and provider"));
	}

	@Test
	void providerWithoutConcreteEvaluatorCannotSatisfyProviderOwnedInvariant() {
		var ir = testIr(SecurityInvariantIds.PARAMETERIZED_VALUES);
		var ex = assertThrows(IllegalStateException.class, () -> MutationExecutionSecurityGate.certify(ir,
				new UncertifiedProvider(), UncertifiedProvider.class.getName(), List.of()));
		assertTrue(ex.getMessage().toLowerCase().contains("no concrete security conformance evaluator"));
	}

	@Test
	void providerConformanceCombinesWithUpstreamEvidence() {
		var ir = testIr(SecurityInvariantIds.AUTHORIZATION_REQUIRED, SecurityInvariantIds.PARAMETERIZED_VALUES);
		var provider = new HonestProvider();
		var certificate = MutationExecutionSecurityGate.certify(ir, provider, provider.getClass().getName(),
				List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED));
		assertTrue(certificate.isSatisfied());
		assertTrue(certificate.preserved().contains(SecurityInvariantIds.AUTHORIZATION_REQUIRED));
		assertTrue(certificate.preserved().contains(SecurityInvariantIds.PARAMETERIZED_VALUES));
	}

	private static ExecutionMutationIR testIr(String... required) {
		var entity = new MutationEntitySchema(new EntityId(1), "Entity", java.util.Set.of(new ColumnId(3)),
				java.util.Map.of(new FieldId(2), new ColumnId(3)), new ColumnId(3));
		var operation = new MutationOperation(entity, MutationKind.CREATE,
				List.of(new MutationFieldValue(new ColumnId(3), "value")), null, null, List.of(new FieldId(2)));
		return ExecutionMutationIR.from(new MutationBatchPlan(List.of(operation), List.of()), List.of(required));
	}

	private static final class HonestProvider implements IMutationSecurityConformanceEvaluator {
		@Override
		public MutationSecurityConformanceResult evaluate(ExecutionMutationIR ir) {
			return new MutationSecurityConformanceResult(getClass().getName(),
					List.of(SecurityInvariantIds.PARAMETERIZED_VALUES), List.of());
		}
	}

	private static final class UncertifiedProvider {
	}
}
