package com.foundgine.runtime;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.*;
import com.foundgine.core.execution.security.IProviderSecurityConformanceEvaluator;
import com.foundgine.core.execution.security.ProviderSecurityConformanceResult;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.authorization.AllowAllSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.core.semantic.security.SecurityInvariantRegistry;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.atomic.AtomicInteger;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of C# {@code Foundgine.E2E.Tests.PlanCacheTests}: proves that provider
 * compilation may be cached without bypassing semantic authorization or
 * removing runtime authorization predicates.
 */
class PlanCacheParityTest {

	private static final EntityId CUSTOMER = EntityId.create("Customer");
	private static final FieldId CUSTOMER_ID = FieldId.create("Customer", "Id");
	private static final FieldId CUSTOMER_TENANT_ID = FieldId.create("Customer", "TenantId");

	@Test
	void repeatedAuthorizedRequestReusesCompiledProviderPlan() {
		var compiler = new CountingCompiler();
		var cache = new MemoryProviderPlanCache();
		var engine = new FoundgineEngine(
				new FoundgineOptions().model(model()).authorizationPolicy(new TenantPolicy()).planCache(cache),
				compiler, new TestExecutionProvider());

		var request = createCustomerRequest(null);

		engine.executeAsync(request, new ExecutionContext(Map.of("user.TenantId", 7))).toCompletableFuture().join();
		engine.executeAsync(request, new ExecutionContext(Map.of("user.TenantId", 42))).toCompletableFuture().join();

		assertEquals(1, compiler.count.get());
	}

	@Test
	void authorizationIsStillEvaluatedBeforeCacheLookup() {
		var compiler = new CountingCompiler();
		var policy = new CountingPolicy();
		var engine = new FoundgineEngine(
				new FoundgineOptions().model(model()).authorizationPolicy(policy).planCache(new MemoryProviderPlanCache()),
				compiler, new TestExecutionProvider());

		var request = createCustomerRequest(null);
		int checksBeforeExecution = policy.entityReadChecks.get();

		engine.executeAsync(request).toCompletableFuture().join();
		engine.executeAsync(request).toCompletableFuture().join();

		assertEquals(checksBeforeExecution + 2, policy.entityReadChecks.get());
		assertEquals(1, compiler.count.get());
	}

	@Test
	void differentRequestValuesDoNotShareAnExactPlanCacheEntry() {
		var compiler = new CountingCompiler();
		var engine = new FoundgineEngine(
				new FoundgineOptions().model(model()).authorizationPolicy(new TenantPolicy())
						.planCache(new MemoryProviderPlanCache()),
				compiler, new TestExecutionProvider());

		engine.executeAsync(createCustomerRequest(7)).toCompletableFuture().join();
		engine.executeAsync(createCustomerRequest(42)).toCompletableFuture().join();

		assertEquals(2, compiler.count.get());
	}

	private static SemanticModel model() {
		return new SemanticModelBuilder().entity(CUSTOMER, "Customer",
				e -> e.identity(CUSTOMER_ID, "Id").field(CUSTOMER_ID, "Id", Long.class)
						.field(CUSTOMER_TENANT_ID, "TenantId", Long.class))
				.build();
	}

	private static SemanticRequest createCustomerRequest(Integer tenantId) {
		SemanticQueryOptions options = tenantId == null ? null
				: new SemanticQueryOptions(new SemanticFieldFilter(CUSTOMER_TENANT_ID, SemanticFilterOperator.EQ, tenantId),
						List.of(), null, null, null);
		return new SemanticRequest(CUSTOMER, List.of(new SemanticSelection(CUSTOMER_ID, null, List.of())), options, null);
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

	private static final class CountingPolicy extends AllowAllSemanticAuthorizationPolicy {
		final AtomicInteger entityReadChecks = new AtomicInteger();

		@Override
		public AuthorizationDecision getEntityAccess(EntityId entityId, AuthorizationOperation operation) {
			if (operation == AuthorizationOperation.READ)
				entityReadChecks.incrementAndGet();
			return AuthorizationDecision.ALLOWED;
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
