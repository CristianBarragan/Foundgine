package com.foundgine.core.semantic.security.execution;

import com.foundgine.core.semantic.security.warrants.CapabilityGrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantConstraints;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Mirrors the C# authority-partition rails: authority-bearing cache keys must
 * never alias.
 */
class SecurityAuthorityPartitionRailsParityTest {
	@Test
	void subjectChangeChangesPartition() {
		var first = context("agent-a", "foundgine", "tenant-a", "customer/1", warrant("w1", "n1"));
		var second = new SecurityExecutionContext(first.warrant(), "agent-b", first.audience(), first.tenant(),
				first.resourceScope());
		assertNotEquals(first.authorityCachePartition(), second.authorityCachePartition());
	}

	@Test
	void audienceChangeChangesPartition() {
		var first = context("agent", "audience-a", "tenant-a", null, warrant("w1", "n1"));
		var second = new SecurityExecutionContext(first.warrant(), first.subject(), "audience-b", first.tenant(),
				first.resourceScope());
		assertNotEquals(first.authorityCachePartition(), second.authorityCachePartition());
	}

	@Test
	void tenantChangeChangesPartition() {
		var first = context("agent", "foundgine", "tenant-a", null, warrant("w1", "n1"));
		var second = new SecurityExecutionContext(first.warrant(), first.subject(), first.audience(), "tenant-b",
				first.resourceScope());
		assertNotEquals(first.authorityCachePartition(), second.authorityCachePartition());
	}

	@Test
	void resourceScopeChangeChangesPartition() {
		var first = context("agent", "foundgine", "tenant-a", "customer/1", warrant("w1", "n1"));
		var second = new SecurityExecutionContext(first.warrant(), first.subject(), first.audience(), first.tenant(),
				"customer/2");
		assertNotEquals(first.authorityCachePartition(), second.authorityCachePartition());
	}

	@Test
	void warrantDigestChangeChangesPartition() {
		var first = context("agent", "foundgine", "tenant-a", "customer/1", warrant("w1", "n1"));
		var second = context("agent", "foundgine", "tenant-a", "customer/1", warrant("w2", "n2"));
		assertNotEquals(first.authorityCachePartition(), second.authorityCachePartition());
		assertTrue(first.authorityCachePartition().contains(first.warrant().digest()));
		assertTrue(second.authorityCachePartition().contains(second.warrant().digest()));
	}

	@Test
	void nullAndExplicitContextValuesDoNotAlias() {
		var w = warrant("w", "n");
		var unrestricted = new SecurityExecutionContext(w, "agent", "foundgine");
		var tenant = new SecurityExecutionContext(w, "agent", "foundgine", "tenant-a", null);
		var resource = new SecurityExecutionContext(w, "agent", "foundgine", null, "customer/1");
		assertNotEquals(unrestricted.authorityCachePartition(), tenant.authorityCachePartition());
		assertNotEquals(unrestricted.authorityCachePartition(), resource.authorityCachePartition());
		assertNotEquals(tenant.authorityCachePartition(), resource.authorityCachePartition());
	}

	@Test
	void delimiterCharactersCannotAliasDifferentFields() {
		var w = warrant("w", "n");
		var first = context("agent|a", "foundgine", "tenant-a", "customer/1", w);
		var second = context("agent", "a|foundgine", "tenant-a", "customer/1", w);
		assertNotEquals(first.authorityCachePartition(), second.authorityCachePartition());
	}

	@Test
	void sameWarrantAndContextPartitionIdentically() {
		var w = warrant("same", "nonce");
		var first = context("agent", "foundgine", "tenant-a", "customer/1", w);
		var second = context("agent", "foundgine", "tenant-a", "customer/1", w);
		assertEquals(first.authorityCachePartition(), second.authorityCachePartition());
	}

	private static SecurityExecutionContext context(String subject, String audience, String tenant, String resource,
			SecurityWarrant w) {
		return new SecurityExecutionContext(w, subject, audience, tenant, resource);
	}

	private static SecurityWarrant warrant(String id, String nonce) {
		return SecurityWarrant.ofDefaults(id, "issuer", "agent", "foundgine",
				List.of(new CapabilityGrant("Customer.read", "read")), new SecurityWarrantConstraints(),
				Instant.now().minusSeconds(60), Instant.now().plusSeconds(3600), nonce, "key-1", null, new byte[0]);
	}
}
