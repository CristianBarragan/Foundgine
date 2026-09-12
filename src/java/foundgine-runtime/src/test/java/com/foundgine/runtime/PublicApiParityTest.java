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
import java.util.Map;
import java.util.concurrent.atomic.AtomicInteger;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Mirrors C# {@code Foundgine.E2E.Tests.PublicApiTests}: the application-facing
 * facade must not require callers to manually orchestrate resolution,
 * authorization, planning, or provider compilation.
 *
 * <p>
 * Java has no {@code Microsoft.Extensions.DependencyInjection} equivalent built
 * in, so this ports through {@link FoundgineServiceRegistry} +
 * {@link FoundgineServiceCollectionExtensions#addFoundgine}, which is the
 * framework-neutral Java counterpart of the C# {@code AddFoundgine} DI
 * extension methods.
 */
class PublicApiParityTest {

	@Test
	void plainFoundgineHasNoOptionalCapabilitiesEnabledByDefault() {
		var options = new FoundgineOptions();

		assertTrue(options.enabledCapabilities().isEmpty());
	}

	@Test
	void publicFacadeExecutesTheCorePipelineThroughDi() {
		var model = new SemanticModelBuilder()
				.entity(EntityId.create("Customer"), "Customer", e -> e.identity(FieldId.create("Customer", "Id"), "Id")
						.field(FieldId.create("Customer", "Name"), "Name", String.class))
				.build();
		var policy = new AllowAllSemanticAuthorizationPolicy();
		var compiler = new TestProviderPlanCompiler();
		var provider = new TestExecutionProvider();

		var services = new FoundgineServiceRegistry();
		FoundgineServiceCollectionExtensions.addFoundgine(services, model, policy, compiler, provider);

		var engine = services.getRequiredService(IFoundgine.class);

		var request = new SemanticRequest(EntityId.create("Customer"),
				List.of(new SemanticSelection(FieldId.create("Customer", "Name"), null, List.of())), null, null);

		var result = engine.executeAsync(request).toCompletableFuture().join();

		assertEquals(1, result.rows().size());
		assertEquals(1, compiler.compiledCount.get());
		assertEquals(1, provider.executionCount.get());
		assertEquals("Alice", result.rows().get(0).values().get("Name"));
	}

	private static final class TestProviderPlanCompiler implements IProviderPlanCompiler,
			ISecurityInvariantProviderCompiler, IProviderSecurityConformanceEvaluator {
		final AtomicInteger compiledCount = new AtomicInteger();

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
			compiledCount.incrementAndGet();
			return new TestPlan();
		}
	}

	private static final class TestExecutionProvider implements IExecutionProvider {
		final AtomicInteger executionCount = new AtomicInteger();

		@Override
		public java.util.concurrent.CompletionStage<ExecutionResult> executeAsync(ProviderPlan plan,
				ExecutionContext context, CancellationToken cancellationToken) {
			executionCount.incrementAndGet();
			return java.util.concurrent.CompletableFuture
					.completedFuture(new ExecutionResult(List.of(new ExecutionRow(Map.of("Name", "Alice")))));
		}
	}

	private static final class TestPlan extends ProviderPlan {
		TestPlan() {
			super("test");
		}
	}
}
