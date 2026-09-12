package com.foundgine.core.semantic.security.warrants;

import org.junit.jupiter.api.Test;

import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.PrivateKey;
import java.security.interfaces.RSAPublicKey;
import java.time.Instant;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.stream.IntStream;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;

/**
 * Port of
 * {@code Foundgine.Core.Semantic.Tests.Security.Warrants.SecurityWarrantGuardRailsPenetrationTests}.
 *
 * <p>Guard-rail tests for authority monotonicity, fail-closed context handling,
 * canonicalization and replay behavior. These tests intentionally attack the
 * boundaries rather than only exercising successful authorization paths.
 */
class SecurityWarrantGuardRailsPenetrationParityTest {

	@Test
	void tenantRestrictionFailsClosedWhenRuntimeTenantIsMissing() {
		var warrant = createWarrant(null, null, new SecurityWarrantConstraints(List.of("tenant-a"), null, null, null,
				null, null), "warrant", "nonce");

		assertFalse(SecurityWarrantAuthorization.allows(warrant, warrant.subject(), warrant.audience(),
				"Customer.read", "read", null, "customer/1"));
	}

	@Test
	void tenantRestrictionRejectsADifferentRuntimeTenant() {
		var warrant = createWarrant(null, null, new SecurityWarrantConstraints(List.of("tenant-a"), null, null, null,
				null, null), "warrant", "nonce");

		assertFalse(SecurityWarrantAuthorization.allows(warrant, warrant.subject(), warrant.audience(),
				"Customer.read", "read", "tenant-b", "customer/1"));
	}

	@Test
	void resourceRestrictionFailsClosedWhenRuntimeResourceIsMissing() {
		var warrant = createWarrant(null, null, new SecurityWarrantConstraints(null, null, List.of("customer/1"),
				null, null, null), "warrant", "nonce");

		assertFalse(SecurityWarrantAuthorization.allows(warrant, warrant.subject(), warrant.audience(),
				"Customer.read", "read", "tenant-a", null));
	}

	@Test
	void resourceRestrictionRejectsADifferentRuntimeResource() {
		var warrant = createWarrant(null, null, new SecurityWarrantConstraints(null, null, List.of("customer/1"),
				null, null, null), "warrant", "nonce");

		assertFalse(SecurityWarrantAuthorization.allows(warrant, warrant.subject(), warrant.audience(),
				"Customer.read", "read", "tenant-a", "customer/2"));
	}

	@Test
	void capabilityMatchingIsExactNotPrefixBased() {
		var warrant = createWarrant(null, List.of(new CapabilityGrant("Customer.read", "read")), null, "warrant",
				"nonce");

		assertFalse(SecurityWarrantAuthorization.allows(warrant, warrant.subject(), warrant.audience(),
				"Customer.read.admin", "read", "tenant-a", "customer/1"));
	}

	@Test
	void operationMatchingIsExactNotPrefixBased() {
		var warrant = createWarrant(null, List.of(new CapabilityGrant("Customer.read", "read")), null, "warrant",
				"nonce");

		assertFalse(SecurityWarrantAuthorization.allows(warrant, warrant.subject(), warrant.audience(),
				"Customer.read", "read.all", "tenant-a", "customer/1"));
	}

	@Test
	void unicodeConfusableCapabilityDoesNotMatch() {
		// The second character in the requested capability is Cyrillic 'е'.
		var confusable = "Customer.rеad";
		var warrant = createWarrant(null, List.of(new CapabilityGrant("Customer.read", "read")), null, "warrant",
				"nonce");

		assertFalse(SecurityWarrantAuthorization.allows(warrant, warrant.subject(), warrant.audience(), confusable,
				"read", "tenant-a", "customer/1"));
	}

	@Test
	void canonicalizationIsStableUnderGrantAndScopeOrdering() {
		var now = Instant.now();
		var first = createWarrant(now,
				List.of(new CapabilityGrant("Customer.write", "write", List.of("customer/2", "customer/1")),
						new CapabilityGrant("Customer.read", "read", List.of("customer/3", "customer/1"))),
				new SecurityWarrantConstraints(List.of("tenant-b", "tenant-a"), List.of("Name", "Id"),
						List.of("customer/2", "customer/1"), List.of("write", "read"), null, null),
				"warrant", "nonce");

		var second = new SecurityWarrant(first.id(), first.issuer(), first.subject(), first.audience(),
				List.of(new CapabilityGrant("Customer.read", "read", List.of("customer/1", "customer/3")),
						new CapabilityGrant("Customer.write", "write", List.of("customer/1", "customer/2"))),
				new SecurityWarrantConstraints(List.of("tenant-a", "tenant-b"), List.of("Id", "Name"),
						List.of("customer/1", "customer/2"), List.of("read", "write"), null, null),
				first.issuedAt(), first.expiresAt(), first.nonce(), first.keyId(), first.parentId(),
				first.signature(), first.parentDigest(), first.delegationPath());

		assertEquals(SecurityWarrantCanonicalizer.digest(first), SecurityWarrantCanonicalizer.digest(second));
	}

	@Test
	void changingAnySecuritySemanticChangesTheDigest() {
		var original = createWarrant(null, null, null, "warrant", "nonce");
		var variants = List.of(
				withSubject(original, "another-agent"),
				withAudience(original, "another-audience"),
				withNonce(original, "another-nonce"),
				withKeyId(original, "another-key"),
				withExpiresAt(original, original.expiresAt().plusSeconds(1)),
				withParentId(original, "parent"),
				withParentDigest(original, "parent-digest"),
				withDelegationPath(original, List.of("ancestor")),
				withConstraints(original,
						new SecurityWarrantConstraints(original.constraints().allowedTenants(),
								original.constraints().allowedFields(), original.constraints().resourceScopes(),
								original.constraints().allowedOperations(), 1L,
								original.constraints().maxAmount())));

		for (var variant : variants) {
			assertNotEquals(original.digest(), variant.digest());
		}
	}

	@Test
	void signatureBindsTheSecuritySemanticsNotJustTheIdentifier() throws Exception {
		var pair = rsaKeyPair();
		var original = SecurityWarrantSigner.sign(createWarrant(null, null, null, "warrant", "nonce"),
				(PrivateKey) pair.getPrivate());
		var modified = withConstraints(original,
				new SecurityWarrantConstraints(original.constraints().allowedTenants(),
						original.constraints().allowedFields(), original.constraints().resourceScopes(),
						original.constraints().allowedOperations(), original.constraints().maxResults(),
						new java.math.BigDecimal("1")));

		assertThrows(IllegalStateException.class, () -> SecurityWarrantVerifier.verify(modified,
				resolver(original.keyId(), pair), Instant.now(), null));
	}

	@Test
	void replayConsumptionIsAtomicUnderConcurrency() throws InterruptedException {
		var warrant = createWarrant(null, null, null, "warrant", "nonce");
		var store = new MemorySecurityWarrantReplayStore();
		var successes = new AtomicInteger();

		var threads = IntStream.range(0, 64).mapToObj(i -> new Thread(() -> {
			try {
				SecurityWarrantReplayGuard.consume(warrant, store, Instant.now());
				successes.incrementAndGet();
			} catch (IllegalStateException ignored) {
				// Expected for every contender except the single winner.
			}
		})).toList();

		threads.forEach(Thread::start);
		for (var t : threads) {
			t.join();
		}

		assertEquals(1, successes.get());
	}

	@Test
	void replayKeyIsBoundToBothWarrantIdAndNonce() {
		var store = new MemorySecurityWarrantReplayStore();
		var first = createWarrant(null, null, null, "w1", "n1");
		var second = createWarrant(null, null, null, "w2", "n2");

		SecurityWarrantReplayGuard.consume(first, store, Instant.now());
		SecurityWarrantReplayGuard.consume(second, store, Instant.now());
	}

	@Test
	void expiredWarrantCannotBeConsumedEvenWhenNonceIsNew() {
		var now = Instant.now();
		var warrant = withTimeBounds(createWarrant(now.minusSeconds(7200), null, null, "warrant", "nonce"),
				now.minusSeconds(7200), now.minusSeconds(3600));
		var store = new MemorySecurityWarrantReplayStore();

		assertThrows(IllegalStateException.class, () -> SecurityWarrantReplayGuard.consume(warrant, store, now));
	}

	@Test
	void childAuthorityIsMonotonicForCapabilitiesAndConstraints() {
		var now = Instant.now();
		var parent = createWarrant(now,
				List.of(new CapabilityGrant("Customer.read", "read", List.of("customer/1", "customer/2"))),
				new SecurityWarrantConstraints(List.of("tenant-a"), List.of("Id", "Name"),
						List.of("customer/1", "customer/2"), List.of("read"), 100L, new java.math.BigDecimal("100")),
				"warrant", "nonce");

		var childConstraints = new SecurityWarrantConstraints(List.of("tenant-a"), List.of("Id"),
				List.of("customer/1"), List.of("read"), 10L, new java.math.BigDecimal("10"));
		var childNode = new SecurityWarrant("child", parent.subject(), "agent-child", parent.audience(),
				parent.grants(), childConstraints, parent.issuedAt(), parent.expiresAt().minusSeconds(60),
				parent.nonce(), parent.keyId(), parent.id(), new byte[0], parent.digest(),
				List.of(parent.digest()));

		assertSame(childNode, SecurityWarrantAttenuator.attenuate(parent, childNode, now));
	}

	@Test
	void childCannotRecoverARemovedTenant() {
		var now = Instant.now();
		var parent = createWarrant(now, null,
				new SecurityWarrantConstraints(List.of("tenant-a", "tenant-b"), null, null, null, null, null),
				"warrant", "nonce");
		var attenuated = new SecurityWarrant("child", parent.subject(), "agent-child", parent.audience(),
				parent.grants(), new SecurityWarrantConstraints(List.of("tenant-a"), null, null, null, null, null),
				parent.issuedAt(), parent.expiresAt().minusSeconds(60), parent.nonce(), parent.keyId(), parent.id(),
				new byte[0], parent.digest(), List.of(parent.digest()));
		var grandchild = new SecurityWarrant("grandchild", attenuated.subject(), "agent-grandchild",
				attenuated.audience(), attenuated.grants(),
				new SecurityWarrantConstraints(List.of("tenant-a", "tenant-b"), null, null, null, null, null),
				attenuated.issuedAt(), attenuated.expiresAt(), attenuated.nonce(), attenuated.keyId(),
				attenuated.id(), new byte[0], attenuated.digest(), List.of(parent.digest(), attenuated.digest()));

		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantAttenuator.attenuate(attenuated, grandchild, now));
	}

	private static SecurityWarrant createWarrant(Instant now, List<CapabilityGrant> grants,
			SecurityWarrantConstraints constraints, String id, String nonce) {
		var current = now != null ? now : Instant.now();
		return new SecurityWarrant(id, "issuer", "agent", "foundgine",
				grants != null ? grants : List.of(new CapabilityGrant("Customer.read", "read", List.of("customer/1"))),
				constraints != null ? constraints : SecurityWarrantConstraints.UNRESTRICTED,
				current.minusSeconds(60), current.plusSeconds(3600), nonce, "key-1", null, new byte[0]);
	}

	private static SecurityWarrant withTimeBounds(SecurityWarrant w, Instant issuedAt, Instant expiresAt) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), w.audience(), w.grants(), w.constraints(),
				issuedAt, expiresAt, w.nonce(), w.keyId(), w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withSubject(SecurityWarrant w, String subject) {
		return new SecurityWarrant(w.id(), w.issuer(), subject, w.audience(), w.grants(), w.constraints(),
				w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withAudience(SecurityWarrant w, String audience) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), audience, w.grants(), w.constraints(),
				w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withNonce(SecurityWarrant w, String nonce) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), w.audience(), w.grants(), w.constraints(),
				w.issuedAt(), w.expiresAt(), nonce, w.keyId(), w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withKeyId(SecurityWarrant w, String keyId) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), w.audience(), w.grants(), w.constraints(),
				w.issuedAt(), w.expiresAt(), w.nonce(), keyId, w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withExpiresAt(SecurityWarrant w, Instant expiresAt) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), w.audience(), w.grants(), w.constraints(),
				w.issuedAt(), expiresAt, w.nonce(), w.keyId(), w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withParentId(SecurityWarrant w, String parentId) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), w.audience(), w.grants(), w.constraints(),
				w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), parentId, w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withParentDigest(SecurityWarrant w, String parentDigest) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), w.audience(), w.grants(), w.constraints(),
				w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), w.parentId(), w.signature(), parentDigest,
				w.delegationPath());
	}

	private static SecurityWarrant withDelegationPath(SecurityWarrant w, List<String> delegationPath) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), w.audience(), w.grants(), w.constraints(),
				w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), w.parentId(), w.signature(), w.parentDigest(),
				delegationPath);
	}

	private static SecurityWarrant withConstraints(SecurityWarrant w, SecurityWarrantConstraints constraints) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), w.audience(), w.grants(), constraints,
				w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static KeyPair rsaKeyPair() throws Exception {
		var generator = KeyPairGenerator.getInstance("RSA");
		generator.initialize(2048);
		return generator.generateKeyPair();
	}

	private static ISecurityWarrantKeyResolver resolver(String id, KeyPair key) {
		return keyId -> {
			if (!id.equals(keyId))
				throw new IllegalStateException("Unknown key");
			return (RSAPublicKey) key.getPublic();
		};
	}
}
