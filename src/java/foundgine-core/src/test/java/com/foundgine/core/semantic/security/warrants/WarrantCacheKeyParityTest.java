package com.foundgine.core.semantic.security.warrants;

import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of C# {@code Foundgine.E2E.Tests.WarrantPlanCacheAttackTests
 * .Different_warrants_cannot_share_an_authority_cache_key}: two warrants that
 * differ only by id/nonce must digest differently, so an authority-bearing
 * provider-plan cache keyed by warrant digest cannot be confused between them.
 */
class WarrantCacheKeyParityTest {

	@Test
	void differentWarrantsCannotShareAnAuthorityCacheKey() {
		Instant now = Instant.now();
		CapabilityGrant grant = new CapabilityGrant("Customer.read", "read");

		SecurityWarrant first = SecurityWarrant.ofDefaults("w1", "issuer", "agent-a", "foundgine", List.of(grant),
				SecurityWarrantConstraints.UNRESTRICTED, now.minusSeconds(60), now.plusSeconds(3600), "n1", "k1", null,
				new byte[0]);
		SecurityWarrant second = SecurityWarrant.ofDefaults("w2", "issuer", "agent-a", "foundgine", List.of(grant),
				SecurityWarrantConstraints.UNRESTRICTED, now.minusSeconds(60), now.plusSeconds(3600), "n2", "k1", null,
				new byte[0]);

		Map<String, String> cache = new HashMap<>();
		cache.put(first.digest(), "authorized-plan-for-w1");

		assertTrue(cache.containsKey(first.digest()));
		assertFalse(cache.containsKey(second.digest()));
	}
}
