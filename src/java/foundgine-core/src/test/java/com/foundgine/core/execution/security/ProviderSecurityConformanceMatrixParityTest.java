package com.foundgine.core.execution.security;

import com.foundgine.core.semantic.security.SecurityInvariantIds;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/** Parity tests for the explicit provider security-guarantee matrix. */
class ProviderSecurityConformanceMatrixParityTest {
	private static final List<String> QUERY_REQUIREMENTS = List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED,
			SecurityInvariantIds.RUNTIME_AUTHORIZATION, SecurityInvariantIds.FIELD_VISIBILITY,
			SecurityInvariantIds.PARAMETERIZED_VALUES, SecurityInvariantIds.PLAN_CACHE_CONTEXT_ISOLATION);

	private static final List<String> TRANSFER_REQUIREMENTS = List.of(SecurityInvariantIds.TENANT_ISOLATION,
			SecurityInvariantIds.RUNTIME_AUTHORIZATION, SecurityInvariantIds.ATOMIC_MUTATION,
			SecurityInvariantIds.IDEMPOTENCY, SecurityInvariantIds.REPLAY_PROTECTION,
			SecurityInvariantIds.AUDIT_REQUIRED, SecurityInvariantIds.EXECUTION_EVIDENCE_REQUIRED);

	@Test
	void queryContractIsSupportedByInMemoryAndSqlProfiles() {
		var matrix = new ProviderSecurityConformanceMatrix().register(FoundgineProviderSecurityProfiles.inMemory())
				.register(FoundgineProviderSecurityProfiles.sql());
		assertTrue(matrix.evaluate("in-memory", QUERY_REQUIREMENTS).isSatisfied());
		assertTrue(matrix.evaluate("sql", QUERY_REQUIREMENTS).isSatisfied());
	}

	@Test
	void transferContractIsSupportedByHighAssurancePostgresProfile() {
		var proof = new ProviderSecurityConformanceMatrix()
				.register(FoundgineProviderSecurityProfiles.postgresTransferFunds())
				.evaluate("postgres-transfer-funds", TRANSFER_REQUIREMENTS);
		assertTrue(proof.isSatisfied());
		assertTrue(proof.missing().isEmpty());
		assertTrue(proof.preserved().contains(SecurityInvariantIds.ATOMIC_MUTATION));
		assertTrue(proof.preserved().contains(SecurityInvariantIds.IDEMPOTENCY));
		assertTrue(proof.preserved().contains(SecurityInvariantIds.REPLAY_PROTECTION));
	}

	@Test
	void genericSqlCannotClaimHighAssuranceMutationGuarantees() {
		var proof = new ProviderSecurityConformanceMatrix().register(FoundgineProviderSecurityProfiles.sql())
				.evaluate("sql", TRANSFER_REQUIREMENTS);
		assertFalse(proof.isSatisfied());
		assertTrue(proof.missing().contains(SecurityInvariantIds.ATOMIC_MUTATION));
		assertTrue(proof.missing().contains(SecurityInvariantIds.IDEMPOTENCY));
		assertTrue(proof.missing().contains(SecurityInvariantIds.AUDIT_REQUIRED));
	}

	@Test
	void unknownProviderFailsClosed() {
		var matrix = new ProviderSecurityConformanceMatrix().register(FoundgineProviderSecurityProfiles.sql());
		assertThrows(java.util.NoSuchElementException.class, () -> matrix.evaluate("unknown", QUERY_REQUIREMENTS));
	}

	@Test
	void providerCannotRegisterUnknownInvariant() {
		var profile = new ProviderSecurityConformanceProfile("hostile-provider", List.of("security.fake"), List.of());
		assertThrows(IllegalStateException.class, () -> new ProviderSecurityConformanceMatrix().register(profile));
	}

	@Test
	void lyingProviderCannotSatisfyUnclaimedRequirement() {
		var matrix = new ProviderSecurityConformanceMatrix().register(new ProviderSecurityConformanceProfile(
				"hostile-provider", List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED),
				List.of("hostile provider claims it preserves authorization only")));
		var proof = matrix.evaluate("hostile-provider",
				List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED, SecurityInvariantIds.TENANT_ISOLATION));
		assertFalse(proof.isSatisfied());
		assertTrue(proof.missing().contains(SecurityInvariantIds.TENANT_ISOLATION));
	}

	@Test
	void providerClaimsCannotInventUnknownInvariants() {
		assertThrows(IllegalStateException.class,
				() -> new ProviderSecurityConformanceMatrix().register(new ProviderSecurityConformanceProfile(
						"hostile-provider",
						List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED, "security.provider-lied"), List.of())));
	}
}
