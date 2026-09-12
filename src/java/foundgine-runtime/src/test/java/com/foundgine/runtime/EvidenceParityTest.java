package com.foundgine.runtime;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.*;
import com.foundgine.core.execution.security.IProviderSecurityConformanceEvaluator;
import com.foundgine.core.execution.security.ProviderSecurityConformanceResult;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.authorization.AllowAllSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.security.SecurityInvariantRegistry;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of C# {@code Foundgine.E2E.Tests.EvidenceTests
 * .Public_engine_enriches_provider_evidence_with_intent_and_authorization_fingerprints}.
 *
 * <p>
 * The SQL-execution-based cases in the C# original
 * ({@code Sql_execution_returns_provider_neutral_evidence},
 * {@code Equivalent_sql_plans_produce_the_same_plan_fingerprint}) are ported
 * separately in
 * {@code com.foundgine.providers.storage.sql.SqlExecutionEvidenceParityTest},
 * following the same opt-in real-PostgreSQL pattern already used by
 * {@code SqlExecutionPostgresE2ETest} / {@code UpsertParityTest} (Java has no
 * embedded-database test dependency), since this test only needs the
 * facade/DI enrichment path and no live database.
 */
class EvidenceParityTest {

	@Test
	void publicEngineEnrichesProviderEvidenceWithIntentAndAuthorizationFingerprints() {
		var model = new SemanticModelBuilder()
				.entity(EntityId.create("Customer"), "Customer", e -> e.identity(FieldId.create("Customer", "Id"), "Id")
						.field(FieldId.create("Customer", "Name"), "Name", String.class))
				.build();
		var policy = new AllowAllSemanticAuthorizationPolicy();
		var compiler = new CapturingEvidenceCompiler();
		var provider = new CapturingEvidenceProvider();

		var services = new FoundgineServiceRegistry();
		FoundgineServiceCollectionExtensions.addFoundgine(services, model, policy, compiler, provider);
		var engine = services.getRequiredService(IFoundgine.class);

		var request = new SemanticRequest(EntityId.create("Customer"),
				List.of(new SemanticSelection(FieldId.create("Customer", "Id"), null, List.of())), null, null);

		var result = engine.executeAsync(request).toCompletableFuture().join();

		assertNotNull(result.evidence());
		assertFalse(isBlank(result.evidence().intentFingerprint()));
		assertFalse(isBlank(result.evidence().authorizationFingerprint()));
		assertNotEquals(result.evidence().intentFingerprint(), result.evidence().authorizationFingerprint());
		assertNotNull(result.receipt());
		assertEquals(result.evidence().planFingerprint(), result.receipt().planFingerprint());
		assertEquals(result.evidence().provider(), result.receipt().provider());
		assertFalse(isBlank(result.receipt().resultFingerprint()));
	}

	private static boolean isBlank(String value) {
		return value == null || value.isBlank();
	}

	private static final class CapturingEvidenceCompiler
			implements IProviderPlanCompiler, ISecurityInvariantProviderCompiler, IProviderSecurityConformanceEvaluator {

		@Override
		public java.util.Collection<String> preservedSecurityInvariants() {
			return SecurityInvariantRegistry.allInvariants().stream().map(x -> x.id()).toList();
		}

		@Override
		public ProviderSecurityConformanceResult evaluate(ExecutionIR ir, ProviderPlan plan) {
			var required = ir.requiredSecurityInvariants();
			return new ProviderSecurityConformanceResult(plan.provider(), required, required, List.of());
		}

		@Override
		public ProviderPlan compile(ExecutionIR ir) {
			return new TestProviderPlan();
		}
	}

	private static final class CapturingEvidenceProvider implements IExecutionProvider {
		@Override
		public CompletionStage<ExecutionResult> executeAsync(ProviderPlan plan, ExecutionContext context,
				CancellationToken cancellationToken) {
			var evidence = ExecutionEvidenceFactory.create("test", "plan", List.of(), 0, 0, null);
			return CompletableFuture.completedFuture(new ExecutionResult(List.of(), null, evidence, null));
		}
	}

	private static final class TestProviderPlan extends ProviderPlan {
		TestProviderPlan() {
			super("test");
		}
	}
}
