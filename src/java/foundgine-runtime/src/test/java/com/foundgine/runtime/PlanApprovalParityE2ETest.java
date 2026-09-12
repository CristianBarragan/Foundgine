package com.foundgine.runtime;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.*;
import com.foundgine.core.execution.security.IProviderSecurityConformanceEvaluator;
import com.foundgine.core.execution.security.ProviderSecurityConformanceResult;
import com.foundgine.core.execution.ISecurityInvariantProviderCompiler;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.authorization.AllowAllSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.security.SecurityInvariantRegistry;
import com.foundgine.core.execution.ProviderPlan;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;

import static org.junit.jupiter.api.Assertions.*;

/** Mirrors C# PlanApprovalTests at the Java Runtime boundary. */
class PlanApprovalParityE2ETest {
	@Test
	void approvalExecutesWhenCurrentPlanMatchesApprovedPlan() {
		var provider = new CountingProvider();
		var engine = createEngine(provider);
		var approval = engine.approvePlan(request(), "human@example");

		var result = engine.executeApprovedAsync(approval).toCompletableFuture().join();

		assertEquals(1, provider.count.get());
		assertNotNull(result);
		assertNotNull(result.receipt());
		assertEquals(approval.approvalId(), result.receipt().approvalId());
		assertEquals("human@example", result.receipt().approvedBy());
		assertEquals(approval.planFingerprint(), result.receipt().planFingerprint());
		assertFalse(result.receipt().resultFingerprint().isBlank());
	}

	@Test
	void approvalRejectsWhenSemanticVersionChanges() {
		var provider = new CountingProvider();
		var engine = createEngine(provider);
		var approval = engine.approvePlan(request(), "human@example");
		var tampered = new PlanApproval(approval.request(), approval.approvalId(), approval.planFingerprint(),
				"sha256:changed", approval.capabilityContractVersion(), approval.capabilityVersion(),
				approval.intentVersion(), approval.planVersion(), approval.approvedBy(), approval.approvedAt());

		assertThrows(IllegalStateException.class, () -> engine.executeApprovedAsync(tampered));
		assertEquals(0, provider.count.get());
	}

	@Test
	void approvalRejectsWhenPlanFingerprintChanges() {
		var provider = new CountingProvider();
		var engine = createEngine(provider);
		var approval = engine.approvePlan(request(), "human@example");
		var tampered = new PlanApproval(approval.request(), approval.approvalId(),
				approval.planFingerprint() + "-tampered", approval.semanticModelVersion(),
				approval.capabilityContractVersion(), approval.capabilityVersion(), approval.intentVersion(),
				approval.planVersion(), approval.approvedBy(), approval.approvedAt());

		assertThrows(IllegalStateException.class, () -> engine.executeApprovedAsync(tampered));
		assertEquals(0, provider.count.get());
	}

	private static FoundgineEngine createEngine(CountingProvider provider) {
		var model = new SemanticModelBuilder()
				.entity(EntityId.create("Customer"), "Customer", e -> e.identity(FieldId.create("Customer", "Id"), "Id")
						.field(FieldId.create("Customer", "Name"), "Name", String.class))
				.build();
		return new FoundgineEngine(model, new AllowAllSemanticAuthorizationPolicy(),
				new com.foundgine.core.semantic.planning.Planner(), new TestCompiler(), provider);
	}

	private static SemanticRequest request() {
		return new SemanticRequest(EntityId.create("Customer"),
				List.of(new SemanticSelection(FieldId.create("Customer", "Id"), null, List.of())), null, null);
	}

	private static final class TestCompiler implements IProviderPlanCompiler, IProviderSecurityConformanceEvaluator,
			ISecurityInvariantProviderCompiler {
		@Override
		public ProviderPlan compile(ExecutionIR ir) {
			return new TestPlan();
		}

		@Override
		public java.util.Collection<String> preservedSecurityInvariants() {
			return SecurityInvariantRegistry.allInvariants().stream().map(x -> x.id()).toList();
		}

		@Override
		public ProviderSecurityConformanceResult evaluate(ExecutionIR ir, ProviderPlan plan) {
			var required = ir.requiredSecurityInvariants();
			return new ProviderSecurityConformanceResult(plan.provider(), required, required, List.of());
		}
	}

	private static final class TestPlan extends ProviderPlan {
		TestPlan() {
			super("test");
		}
	}

	private static final class CountingProvider implements IExecutionProvider {
		final AtomicInteger count = new AtomicInteger();

		@Override
		public java.util.concurrent.CompletionStage<ExecutionResult> executeAsync(ProviderPlan plan,
				ExecutionContext context, CancellationToken cancellationToken) {
			count.incrementAndGet();
			var evidence = com.foundgine.core.execution.ExecutionEvidenceFactory.create("test", "placeholder",
					List.of(1), 1, 0, null);
			return java.util.concurrent.CompletableFuture.completedFuture(
					new ExecutionResult(List.of(new ExecutionRow(java.util.Map.of("Id", 1))), null, evidence, null));
		}
	}
}
