package com.foundgine.core.semantic.security;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.abstractions.AuthorizationDecision;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.SemanticModelBuilder;
import com.foundgine.core.semantic.SemanticVersionSet;
import com.foundgine.core.semantic.authorization.AllowAllSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.capabilities.SemanticCapability;
import com.foundgine.core.semantic.capabilities.SemanticCapabilityContractDiscovery;
import com.foundgine.core.semantic.capabilities.SemanticCapabilityEffect;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of {@code Foundgine.Core.Semantic.Tests.Security.SecurityInvariantRegistryTests}.
 */
class SecurityInvariantRegistryParityTest {

	@Test
	void registryContainsCanonicalInvariants() {
		var ids = SecurityInvariantRegistry.allInvariants().stream().map(SecurityInvariant::id).toList();

		assertTrue(ids.contains(SecurityInvariantIds.AUTHORIZATION_REQUIRED));
		assertTrue(ids.contains(SecurityInvariantIds.RUNTIME_AUTHORIZATION));
		assertTrue(ids.contains(SecurityInvariantIds.TENANT_ISOLATION));
		assertTrue(ids.contains(SecurityInvariantIds.PLAN_CACHE_CONTEXT_ISOLATION));
		assertTrue(ids.contains(SecurityInvariantIds.ATOMIC_MUTATION));
		assertTrue(ids.contains(SecurityInvariantIds.EXECUTION_EVIDENCE_REQUIRED));
	}

	@Test
	void genericMutationCapabilityHasMinimumSecurityInvariants() {
		var model = new SemanticModelBuilder().entity(new EntityId(1), "Order",
				e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Total", java.math.BigDecimal.class))
				.build();

		var contract = SemanticCapabilityContractDiscovery.describe(model, new AllowAllSemanticAuthorizationPolicy());
		var matches = contract.capabilities().stream().filter(x -> x.id().equals("Order.update")).toList();
		assertEquals(1, matches.size());
		var update = matches.get(0);

		assertTrue(update.effectiveSecurityInvariants().contains(SecurityInvariantIds.AUTHORIZATION_REQUIRED));
		assertTrue(update.effectiveSecurityInvariants().contains(SecurityInvariantIds.RUNTIME_AUTHORIZATION));
		assertTrue(update.effectiveSecurityInvariants().contains(SecurityInvariantIds.FIELD_VISIBILITY));
		assertTrue(update.effectiveSecurityInvariants().contains(SecurityInvariantIds.PARAMETERIZED_VALUES));
		SecurityInvariantContractValidator.ensureValid(update);
	}

	@Test
	void invalidMutatingCapabilityIsRejectedAsAContractViolation() {
		var capability = new SemanticCapability("money.transfer", "Transfer", new EntityId(1),
				AuthorizationDecision.ALLOWED, List.of(), List.of(),
				List.of(new SemanticCapabilityEffect("money.debit", "Debit funds")), List.of(), List.of(), "transfer",
				true, false, SemanticVersionSet.CURRENT_CAPABILITY_VERSION,
				List.of(SecurityInvariantIds.PARAMETERIZED_VALUES));

		var errors = SecurityInvariantContractValidator.validate(capability);

		assertEquals(2, errors.size());
		assertTrue(errors.stream().anyMatch(x -> x.contains(SecurityInvariantIds.RUNTIME_AUTHORIZATION)));
		assertTrue(errors.stream().anyMatch(x -> x.contains(SecurityInvariantIds.AUTHORIZATION_REQUIRED)));
	}

	@Test
	void unknownInvariantIdsFailClosed() {
		var capability = new SemanticCapability("customer.read", "Read Customer", new EntityId(1),
				AuthorizationDecision.ALLOWED, List.of(), List.of(), List.of(), List.of("Name"), List.of(), "read",
				false, false, SemanticVersionSet.CURRENT_CAPABILITY_VERSION, List.of("security.not-real"));

		var errors = SecurityInvariantContractValidator.validate(capability);

		assertEquals(2, errors.size());
		assertTrue(errors.stream().anyMatch(x -> x.contains("security.not-real")));
		assertTrue(errors.stream().anyMatch(x -> x.contains(SecurityInvariantIds.FIELD_VISIBILITY)));
	}

	@Test
	void contractValidationIsMachineReadableAndProviderNeutral() throws Exception {
		var model = new SemanticModelBuilder().entity(new EntityId(1), "Customer",
				e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Name", String.class)).build();

		var contract = SemanticCapabilityContractDiscovery.describe(model, new AllowAllSemanticAuthorizationPolicy());
		SecurityInvariantContractValidator.ensureContractValid(contract);

		var json = new ObjectMapper().writeValueAsString(contract);
		assertTrue(json.contains(SecurityInvariantIds.AUTHORIZATION_REQUIRED));
		assertFalse(json.toLowerCase().contains("npgsql"));
		assertFalse(json.toLowerCase().contains("select "));
	}
}
