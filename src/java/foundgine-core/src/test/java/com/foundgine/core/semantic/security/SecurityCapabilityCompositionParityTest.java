package com.foundgine.core.semantic.security;

import com.foundgine.core.abstractions.AuthorizationDecision;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.semantic.capabilities.SemanticCapability;
import com.foundgine.core.semantic.security.warrants.CapabilityGrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantConstraints;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

class SecurityCapabilityCompositionParityTest {
	private static SemanticCapability capability(String id, String operation) {
		boolean hasSideEffects = operation.equals("write");
		List<String> invariants = hasSideEffects
				? List.of(SecurityInvariantIds.RUNTIME_AUTHORIZATION, SecurityInvariantIds.AUTHORIZATION_REQUIRED)
				: List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
		return new SemanticCapability(id, id, EntityId.create(id), AuthorizationDecision.ALLOWED, List.of(), List.of(),
				List.of(), List.of(), List.of(), operation, hasSideEffects, hasSideEffects, 1, invariants);
	}

	private static SecurityWarrant warrant() {
		var now = Instant.now();
		return SecurityWarrant
				.ofDefaults("w1", "issuer", "agent", "audience",
						List.of(new CapabilityGrant("orders.read", "read", List.of("orders/*"))),
						new SecurityWarrantConstraints(List.of("tenant-a"), List.of(), List.of("orders/*"),
								List.of("read"), null, null),
						now.minusSeconds(10), now.plusSeconds(300), "nonce", "key", null, new byte[0]);
	}

	@Test
	void duplicateCapabilitiesDoNotBroadenComposedAuthority() {
		var result = SecurityCapabilityComposition.validate(
				List.of(capability("orders.read", "read"), capability("orders.read", "read")), warrant(), "agent",
				"audience", "tenant-a", "orders/*");

		assertTrue(result.isSatisfied());
		assertEquals(1, result.components().size());
	}

	@Test
	void crossTenantCompositionFailsClosed() {
		var result = SecurityCapabilityComposition.validate(List.of(capability("orders.read", "read")), warrant(),
				"agent", "audience", "tenant-b", "orders/*");

		assertFalse(result.isSatisfied());
		assertTrue(result.failureReason().contains("tenant"));
	}

	@Test
	void writeCapabilityCannotBeSmuggledIntoReadOnlyWarrant() {
		var result = SecurityCapabilityComposition.validate(List.of(capability("orders.write", "write")), warrant(),
				"agent", "audience", "tenant-a", "orders/*");

		assertFalse(result.isSatisfied());
	}
}
