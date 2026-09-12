package com.foundgine.runtime;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.*;
import com.foundgine.core.execution.security.IProviderSecurityConformanceEvaluator;
import com.foundgine.core.execution.security.ProviderSecurityConformanceResult;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.authorization.AllowAllSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.authorization.ISemanticAuthorizationPolicy;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationException;
import com.foundgine.core.semantic.security.SecurityInvariantRegistry;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.atomic.AtomicInteger;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of C# {@code Foundgine.E2E.Tests.ContextSafePlanCacheTests}: security
 * invariants for provider-plan caching. The cache may reuse a compiled plan
 * across runtime contexts only when the plan itself contains the runtime
 * context lookup as a provider-independent predicate.
 */
class ContextSafePlanCacheParityTest {

	private static final EntityId CUSTOMER = EntityId.create("Customer");
	private static final FieldId CUSTOMER_ID = FieldId.create("Customer", "Id");

	@Test
	void sameAuthorizedShapeReusesPlanAcrossRuntimeContexts() {
		var compiler = new CountingCompiler();
		var engine = createEngine(compiler, new TenantPolicy(), null);
		var request = createCustomerRequest();

		engine.executeAsync(request, new ExecutionContext(Map.of("user.TenantId", 7))).toCompletableFuture().join();
		engine.executeAsync(request, new ExecutionContext(Map.of("user.TenantId", 42))).toCompletableFuture().join();

		assertEquals(1, compiler.count.get());
	}

	@Test
	void runtimeContextValuesAreNotPartOfThePlanCacheKey() {
		var compiler = new CountingCompiler();
		var engine = createEngine(compiler, new TenantPolicy(), null);
		var request = createCustomerRequest();

		engine.executeAsync(request, new ExecutionContext(Map.of("user.TenantId", 7))).toCompletableFuture().join();
		engine.executeAsync(request, new ExecutionContext(Map.of("user.TenantId", 8))).toCompletableFuture().join();

		assertEquals(1, compiler.count.get());
	}

	@Test
	void differentAuthorizationPredicatesDoNotShareAProviderPlan() {
		var compiler = new CountingCompiler();
		var cache = new MemoryProviderPlanCache();
		var model = model();

		var first = new FoundgineEngine(
				new FoundgineOptions().model(model).authorizationPolicy(new TenantPolicy()).planCache(cache), compiler,
				new TestExecutionProvider());
		var second = new FoundgineEngine(
				new FoundgineOptions().model(model).authorizationPolicy(new RegionPolicy()).planCache(cache), compiler,
				new TestExecutionProvider());

		first.executeAsync(createCustomerRequest()).toCompletableFuture().join();
		second.executeAsync(createCustomerRequest()).toCompletableFuture().join();

		assertEquals(2, compiler.count.get());
	}

	@Test
	void deniedRequestsNeverCompileOrReadACachedProviderPlan() {
		var compiler = new CountingCompiler();
		var cache = new MemoryProviderPlanCache();
		var engine = createEngine(compiler, new DenyCustomerPolicy(), cache);

		assertThrows(SemanticAuthorizationException.class, () -> engine.executeAsync(createCustomerRequest()));

		assertEquals(0, compiler.count.get());
		assertNull(cache.tryGet("unrelated"));
	}

	private static FoundgineEngine createEngine(CountingCompiler compiler, ISemanticAuthorizationPolicy policy,
			IProviderPlanCache cache) {
		return new FoundgineEngine(new FoundgineOptions().model(model()).authorizationPolicy(policy)
				.planCache(cache != null ? cache : new MemoryProviderPlanCache()), compiler, new TestExecutionProvider());
	}

	private static SemanticModel model() {
		return new SemanticModelBuilder()
				.entity(CUSTOMER, "Customer", e -> e.identity(CUSTOMER_ID, "Id").field(CUSTOMER_ID, "Id", Long.class))
				.build();
	}

	private static SemanticRequest createCustomerRequest() {
		return new SemanticRequest(CUSTOMER, List.of(new SemanticSelection(CUSTOMER_ID, null, List.of())), null, null);
	}

	private static final class TenantPolicy extends AllowAllSemanticAuthorizationPolicy {
		@Override
		public AuthorizationPredicate getPredicate(EntityId entityId, AuthorizationOperation operation) {
			return operation == AuthorizationOperation.READ && entityId.equals(CUSTOMER)
					? AuthorizationPredicate.equal(
							AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "TenantId"),
							AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("user"), "TenantId"))
					: null;
		}
	}

	private static final class RegionPolicy extends AllowAllSemanticAuthorizationPolicy {
		@Override
		public AuthorizationPredicate getPredicate(EntityId entityId, AuthorizationOperation operation) {
			return operation == AuthorizationOperation.READ && entityId.equals(CUSTOMER)
					? AuthorizationPredicate.equal(
							AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "RegionId"),
							AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("user"), "RegionId"))
					: null;
		}
	}

	private static final class DenyCustomerPolicy extends AllowAllSemanticAuthorizationPolicy {
		@Override
		public AuthorizationDecision getEntityAccess(EntityId entityId, AuthorizationOperation operation) {
			return entityId.equals(CUSTOMER) && operation == AuthorizationOperation.READ ? AuthorizationDecision.DENIED
					: AuthorizationDecision.ALLOWED;
		}
	}

	private static final class CountingCompiler
			implements IProviderPlanCompiler, ISecurityInvariantProviderCompiler, IProviderSecurityConformanceEvaluator {
		final AtomicInteger count = new AtomicInteger();

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
			count.incrementAndGet();
			return new TestPlan();
		}
	}

	private static final class TestExecutionProvider implements IExecutionProvider {
		@Override
		public CompletionStage<ExecutionResult> executeAsync(ProviderPlan plan, ExecutionContext context,
				CancellationToken cancellationToken) {
			return CompletableFuture.completedFuture(new ExecutionResult(List.of()));
		}
	}

	private static final class TestPlan extends ProviderPlan {
		TestPlan() {
			super("test");
		}
	}
}
