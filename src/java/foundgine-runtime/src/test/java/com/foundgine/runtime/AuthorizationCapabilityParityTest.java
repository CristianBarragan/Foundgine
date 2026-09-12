package com.foundgine.runtime;

import com.foundgine.core.abstractions.AuthorizationAccess;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionIR;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.IExecutionProvider;
import com.foundgine.core.execution.IProviderPlanCompiler;
import com.foundgine.core.execution.ISecurityInvariantProviderCompiler;
import com.foundgine.core.execution.ProviderPlan;
import com.foundgine.core.execution.security.IProviderSecurityConformanceEvaluator;
import com.foundgine.core.execution.security.ProviderSecurityConformanceResult;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticModelBuilder;
import com.foundgine.core.semantic.authorization.AllowAllSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.security.SecurityInvariantRegistry;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of {@code Foundgine.E2E.Tests.AuthorizationCapabilityTests}.
 *
 * <p>
 * <b>Porting decisions:</b>
 * <ul>
 * <li>No Java test anywhere in the repository calls
 * {@code FoundgineEngine.describeCapabilities()} — this is a real, previously
 * untested public entry point, not a duplicate of any existing coverage.</li>
 * <li>The C# original resolves against the archived Banking fixture; since
 * only the {@code Customer} entity's capability is asserted, this test builds
 * a minimal single-entity model inline rather than the full Customer/Account/
 * Transaction chain, following the same "don't invent a shared Banking
 * fixture" approach as the other pipeline ports this round.</li>
 * <li>C#'s {@code customer.Read.Access}/{@code customer.Write.Access}
 * properties become {@code customer.read().access()}/
 * {@code customer.write().access()}: the Java port models a read/write
 * decision as an {@code AuthorizationDecision} record with an
 * {@code access()} component, rather than C#'s {@code Access} property
 * directly on the capability.</li>
 * <li>Test fixture classes ({@code TestProviderPlanCompiler},
 * {@code TestExecutionProvider}, {@code TestPlan}) mirror the ones already
 * established by {@code PlanCacheParityTest} in this same package, kept
 * identical rather than reinvented.</li>
 * </ul>
 */
class AuthorizationCapabilityParityTest {

	private static final EntityId CUSTOMER = EntityId.create("Customer");
	private static final FieldId CUSTOMER_ID = FieldId.create("Customer", "Id");
	private static final FieldId CUSTOMER_NAME = FieldId.create("Customer", "Name");

	@Test
	void publicFacadeExposesPolicyScopedCapabilitiesForCallers() {
		var engine = new FoundgineEngine(
				new FoundgineOptions().model(model()).authorizationPolicy(new ReadOnlyCustomerPolicy()),
				new TestProviderPlanCompiler(), new TestExecutionProvider());

		var capabilities = engine.describeCapabilities();
		var customer = capabilities.entities().stream().filter(x -> x.entityId().equals(CUSTOMER)).findFirst()
				.orElseThrow();

		assertEquals(AuthorizationAccess.ALLOWED, customer.read().access());
		assertEquals(AuthorizationAccess.DENIED, customer.write().access());
		assertTrue(customer.fields().stream()
				.anyMatch(f -> f.name().equals("Name") && f.read().access() == AuthorizationAccess.ALLOWED));
	}

	private static SemanticModel model() {
		return new SemanticModelBuilder().entity(CUSTOMER, "Customer",
				e -> e.identity(CUSTOMER_ID, "Id").field(CUSTOMER_ID, "Id", Long.class)
						.field(CUSTOMER_NAME, "Name", String.class))
				.build();
	}

	private static final class ReadOnlyCustomerPolicy extends AllowAllSemanticAuthorizationPolicy {
		@Override
		public boolean canWriteEntity(EntityId id) {
			return false;
		}

		@Override
		public boolean canWriteField(EntityId entityId, FieldId fieldId) {
			return false;
		}
	}

	private static final class TestProviderPlanCompiler
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
