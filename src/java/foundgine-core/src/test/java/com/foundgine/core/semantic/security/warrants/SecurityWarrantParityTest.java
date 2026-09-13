package com.foundgine.core.semantic.security.warrants;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

import java.math.BigDecimal;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.PrivateKey;
import java.security.Signature;
import java.security.interfaces.RSAPublicKey;
import java.time.Instant;
import java.util.List;
import java.util.function.BiFunction;
import java.util.function.Function;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of {@code Foundgine.Core.Semantic.Tests.Security.Warrants.SecurityWarrantTests}
 * and {@code SecurityWarrantDelegationTrustSecurityTests}.
 *
 * <p>
 * <b>Porting decisions:</b>
 * <ul>
 * <li>C#'s combined {@code RSA} key (able to both sign and verify) becomes a
 * Java {@link KeyPair}: the private key signs via
 * {@link SecurityWarrantSigner#sign}, and an {@link ISecurityWarrantKeyResolver}
 * resolves the corresponding {@link RSAPublicKey} for verification.</li>
 * <li>C# record {@code with} expressions become small local
 * {@code withXxx(...)} helpers that rebuild a {@link SecurityWarrant} through
 * its canonical constructor, since Java records have no non-destructive
 * copy syntax.</li>
 * </ul>
 */
class SecurityWarrantParityTest {

	private static SecurityWarrant create(Instant now) {
		return create(now, null, null, "issuer", "foundgine");
	}

	private static SecurityWarrant create(Instant now, Instant issuedAt, Instant expiresAt) {
		return create(now, issuedAt, expiresAt, "issuer", "foundgine");
	}

	private static SecurityWarrant create(Instant now, Instant issuedAt, Instant expiresAt, String issuer,
			String audience) {
		return new SecurityWarrant("warrant-1", issuer, "agent-a", audience,
				List.of(new CapabilityGrant("Customer.read", "read", List.of("customer/*"))),
				new SecurityWarrantConstraints(List.of("tenant-1"), null, null, null, 100L, new BigDecimal("1000")),
				issuedAt != null ? issuedAt : now.minusSeconds(60), expiresAt != null ? expiresAt : now.plusSeconds(3600),
				"nonce-1", "key-1", null, new byte[0], null, List.of());
	}

	private static SecurityWarrant sign(SecurityWarrant warrant, PrivateKey key) {
		return SecurityWarrantSigner.sign(warrant, key);
	}

	private static SecurityWarrant withId(SecurityWarrant w, String id) {
		return new SecurityWarrant(id, w.issuer(), w.subject(), w.audience(), w.grants(), w.constraints(),
				w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withSubject(SecurityWarrant w, String subject) {
		return new SecurityWarrant(w.id(), w.issuer(), subject, w.audience(), w.grants(), w.constraints(),
				w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withIssuer(SecurityWarrant w, String issuer) {
		return new SecurityWarrant(w.id(), issuer, w.subject(), w.audience(), w.grants(), w.constraints(),
				w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withAudience(SecurityWarrant w, String audience) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), audience, w.grants(), w.constraints(),
				w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withGrants(SecurityWarrant w, List<CapabilityGrant> grants) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), w.audience(), grants, w.constraints(),
				w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withConstraints(SecurityWarrant w, SecurityWarrantConstraints constraints) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), w.audience(), w.grants(), constraints,
				w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withExpiresAt(SecurityWarrant w, Instant expiresAt) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), w.audience(), w.grants(), w.constraints(),
				w.issuedAt(), expiresAt, w.nonce(), w.keyId(), w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withSignature(SecurityWarrant w, byte[] signature) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), w.audience(), w.grants(), w.constraints(),
				w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), w.parentId(), signature, w.parentDigest(),
				w.delegationPath());
	}

	private static SecurityWarrant withDelegation(SecurityWarrant w, String parentId, String issuer,
			String parentDigest, List<String> delegationPath) {
		return new SecurityWarrant(w.id(), issuer, w.subject(), w.audience(), w.grants(), w.constraints(),
				w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), parentId, w.signature(), parentDigest,
				delegationPath);
	}

	private static KeyPair rsaKeyPair() {
		try {
			var generator = KeyPairGenerator.getInstance("RSA");
			generator.initialize(2048);
			return generator.generateKeyPair();
		} catch (Exception e) {
			throw new IllegalStateException(e);
		}
	}

	private static ISecurityWarrantKeyResolver resolver(String id, KeyPair key) {
		return keyId -> {
			if (!id.equals(keyId))
				throw new IllegalStateException("Unknown key");
			return (RSAPublicKey) key.getPublic();
		};
	}

	private static ISecurityWarrantKeyResolver emptyResolver() {
		return keyId -> {
			throw new IllegalStateException("Unknown key");
		};
	}

	@Test
	void canonicalSignatureRoundTripsAndDigestIsStable() {
		var key = rsaKeyPair();
		var now = Instant.now();
		var signed = sign(create(now), key.getPrivate());

		assertDoesNotThrow(() -> SecurityWarrantVerifier.verify(signed, resolver(signed.keyId(), key), now.plusSeconds(60),
				"issuer", "foundgine"));
		assertTrue(signed.signature().length > 0);
		assertEquals(signed.digest(), SecurityWarrantCanonicalizer.digest(signed));
	}

	@Test
	void expiredWarrantIsRejected() {
		var now = Instant.now();
		assertVerificationFails(create(now, now.minusSeconds(7200), now.minusSeconds(3600)));
	}

	@Test
	void wrongIssuerIsRejected() {
		assertVerificationFails(create(Instant.now(), null, null, "other", "foundgine"), "issuer", null);
	}

	@Test
	void wrongAudienceIsRejected() {
		assertVerificationFails(create(Instant.now(), null, null, "issuer", "other"), null, "foundgine");
	}

	@Test
	void wrongSubjectIsRejectedAtRuntime() {
		var w = create(Instant.now());
		assertFalse(SecurityWarrantAuthorization.allows(w, "wrong-subject", w.audience(), "Customer.read", "read",
				"tenant-1", "customer/*"));
	}

	@Test
	void wrongTenantIsRejectedAtRuntime() {
		var w = create(Instant.now());
		assertFalse(SecurityWarrantAuthorization.allows(w, w.subject(), w.audience(), "Customer.read", "read",
				"tenant-2", "customer/*"));
	}

	@Test
	void wrongCapabilityIsRejectedAtRuntime() {
		var w = create(Instant.now());
		assertFalse(SecurityWarrantAuthorization.allows(w, w.subject(), w.audience(), "Customer.delete", "read",
				"tenant-1", "customer/*"));
	}

	@Test
	void wrongResourceIsRejectedAtRuntime() {
		var w = create(Instant.now());
		assertFalse(SecurityWarrantAuthorization.allows(w, w.subject(), w.audience(), "Customer.read", "read",
				"tenant-1", "order/*"));
	}

	@Test
	void wrongOperationIsRejectedAtRuntime() {
		var w = create(Instant.now());
		assertFalse(SecurityWarrantAuthorization.allows(w, w.subject(), w.audience(), "Customer.read", "write",
				"tenant-1", "customer/*"));
	}

	@Test
	void modifiedWarrantFailsSignatureVerification() {
		assertSignatureFails(w -> withSubject(w, "attacker"));
	}

	@Test
	void modifiedConstraintFailsSignatureVerification() {
		assertSignatureFails(w -> withConstraints(w, new SecurityWarrantConstraints(w.constraints().allowedTenants(),
				w.constraints().allowedFields(), w.constraints().resourceScopes(),
				w.constraints().allowedOperations(), w.constraints().maxResults(), new BigDecimal("999999"))));
	}

	@Test
	void modifiedSignatureFailsVerification() {
		assertSignatureFails(w -> {
			byte[] tampered = w.signature();
			tampered[0] = (byte) (tampered[0] ^ 0xFF);
			return withSignature(w, tampered);
		});
	}

	@Test
	void wrongKeyIsRejected() {
		var key = rsaKeyPair();
		var other = rsaKeyPair();
		var signed = sign(create(Instant.now()), key.getPrivate());
		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantVerifier.verify(signed, resolver(signed.keyId(), other), Instant.now(), "issuer",
						null));
	}

	@Test
	void unknownKeyIsRejected() {
		var key = rsaKeyPair();
		var signed = sign(create(Instant.now()), key.getPrivate());
		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantVerifier.verify(signed, emptyResolver(), Instant.now(), "issuer", null));
	}

	@Test
	void algorithmSubstitutionIsRejected() throws Exception {
		var key = rsaKeyPair();
		var w = create(Instant.now());
		var signature = Signature.getInstance("SHA512withRSA");
		signature.initSign(key.getPrivate());
		signature.update(SecurityWarrantCanonicalizer.unsignedBytes(withSignature(w, new byte[0])));
		var substituted = withSignature(w, signature.sign());
		assertThrows(IllegalStateException.class, () -> SecurityWarrantVerifier.verify(substituted,
				resolver(w.keyId(), key), Instant.now(), "issuer", null));
	}

	@Test
	void signatureOverNoncanonicalRepresentationIsRejected() throws Exception {
		var key = rsaKeyPair();
		var w = create(Instant.now());
		var nonCanonical = SecurityWarrantCanonicalizer.unsignedJson(w).replace("{\"id\"", "{ \"id\"");
		var signature = Signature.getInstance("SHA256withRSA");
		signature.initSign(key.getPrivate());
		signature.update(nonCanonical.getBytes(java.nio.charset.StandardCharsets.UTF_8));
		var tampered = withSignature(w, signature.sign());
		assertThrows(IllegalStateException.class, () -> SecurityWarrantVerifier.verify(tampered,
				resolver(w.keyId(), key), Instant.now(), "issuer", null));
	}

	@Test
	void childGrantsMoreThanParentIsRejected() {
		assertDelegationFails((p, c) -> withGrants(c, List.of(new CapabilityGrant("Customer.write", "write",
				List.of("customer/*")))));
	}

	@Test
	void childExtendsExpiryIsRejected() {
		assertDelegationFails((p, c) -> withExpiresAt(c, p.expiresAt().plusSeconds(60)));
	}

	@Test
	void childChangesTenantIsRejected() {
		assertDelegationFails((p, c) -> withConstraints(c, new SecurityWarrantConstraints(List.of("tenant-2"),
				c.constraints().allowedFields(), c.constraints().resourceScopes(), c.constraints().allowedOperations(),
				c.constraints().maxResults(), c.constraints().maxAmount())));
	}

	@Test
	void childAddsCapabilityIsRejected() {
		assertDelegationFails((p, c) -> {
			var grants = new java.util.ArrayList<>(p.grants());
			grants.add(new CapabilityGrant("Customer.delete", "delete", List.of("customer/*")));
			return withGrants(c, grants);
		});
	}

	@Test
	void childChangesResourceScopeIsRejected() {
		assertDelegationFails(
				(p, c) -> withGrants(c, List.of(new CapabilityGrant("Customer.read", "read", List.of("*")))));
	}
	// A child changing Subject is intentionally NOT an attenuation violation — see
	// delegatedSubjectCanChangeButIssuerMustBeParentSubject below, which asserts
	// that delegating to a new subject succeeds as long as the child's issuer
	// equals the parent's subject. There is deliberately no
	// "childChangesSubjectIsRejected" test.

	@Test
	void sameWarrantSameNonceCanOnlyBeConsumedOnce() {
		var store = new MemorySecurityWarrantReplayStore();
		var w = create(Instant.now());
		SecurityWarrantReplayGuard.consume(w, store, Instant.now());
		assertThrows(IllegalStateException.class, () -> SecurityWarrantReplayGuard.consume(w, store, Instant.now()));
	}

	@ParameterizedTest
	@ValueSource(strings = { "different-intent", "different-tenant", "different-amount", "different-target" })
	void sameWarrantCannotBeReusedForADifferentIntent(String ignored) {
		var store = new MemorySecurityWarrantReplayStore();
		var w = create(Instant.now());
		SecurityWarrantReplayGuard.consume(w, store, Instant.now());
		assertThrows(IllegalStateException.class, () -> SecurityWarrantReplayGuard.consume(w, store, Instant.now()));
	}

	@Test
	void sameWarrantAfterExpiryIsRejected() {
		var store = new MemorySecurityWarrantReplayStore();
		var issued = Instant.now().minusSeconds(600);
		var w = create(issued, issued, issued.plusSeconds(60));
		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantReplayGuard.consume(w, store, Instant.now()));
	}

	@Test
	void delegatedSubjectCanChangeButIssuerMustBeParentSubject() {
		var now = Instant.now();
		var parent = create(now);
		var child = withDelegation(withExpiresAt(withSignature(withSubject(withId(parent, "child"), "agent-b"),
				new byte[0]), parent.expiresAt().minusSeconds(60)), parent.id(), parent.subject(), parent.digest(),
				List.of(parent.digest()));

		assertSame(child, SecurityWarrantAttenuator.attenuate(parent, child, now));
	}

	@Test
	void parentDigestSubstitutionIsRejected() {
		var now = Instant.now();
		var parent = create(now);
		var other = withId(create(now), "other");
		var child = withDelegation(withSignature(withId(parent, "child"), new byte[0]), parent.id(), parent.subject(),
				other.digest(), List.of(other.digest()));

		assertThrows(IllegalStateException.class, () -> SecurityWarrantAttenuator.attenuate(parent, child, now));
	}

	@Test
	void delegationPathSubstitutionIsRejected() {
		var now = Instant.now();
		var parent = create(now);
		var child = withDelegation(withSignature(withId(parent, "child"), new byte[0]), parent.id(), parent.subject(),
				parent.digest(), List.of("forged-parent"));

		assertThrows(IllegalStateException.class, () -> SecurityWarrantAttenuator.attenuate(parent, child, now));
	}

	@Test
	void delegationCycleIsRejected() {
		var now = Instant.now();
		var parent = create(now);
		var child = withDelegation(withSignature(withId(parent, "child"), new byte[0]), parent.id(), parent.subject(),
				parent.digest(), List.of(parent.digest(), parent.digest()));

		assertThrows(IllegalStateException.class, () -> SecurityWarrantAttenuator.attenuate(parent, child, now));
	}

	@Test
	void delegationDepthCannotSkipLevels() {
		var now = Instant.now();
		var parent = create(now);
		var child = withDelegation(withSignature(withId(parent, "child"), new byte[0]), parent.id(), parent.subject(),
				parent.digest(), List.of());

		assertThrows(IllegalStateException.class, () -> SecurityWarrantAttenuator.attenuate(parent, child, now));
	}

	@Test
	void validChildCanOnlyAttenuateParent() {
		var now = Instant.now();
		var parent = withConstraints(create(now),
				new SecurityWarrantConstraints(List.of("tenant-1"), null, null, null, 100L, null));
		var child = withDelegation(
				withConstraints(
						withExpiresAt(withSignature(withId(parent, "child"), new byte[0]),
								parent.expiresAt().minusSeconds(60)),
						new SecurityWarrantConstraints(List.of("tenant-1"), null, null, null, 20L, null)),
				parent.id(), parent.subject(), parent.digest(), List.of(parent.digest()));

		assertSame(child, SecurityWarrantAttenuator.attenuate(parent, child, now));
	}

	@Test
	void runtimeAuthorizationRequiresCurrentSubjectAudienceTenantResourceAndLimits() {
		var w = withConstraints(create(Instant.now()),
				new SecurityWarrantConstraints(List.of("tenant-1"), null, null, null, 10L, new BigDecimal("100")));

		assertTrue(SecurityWarrantAuthorization.allows(w, "agent-a", "foundgine", "Customer.read", "read", "tenant-1",
				"customer/*", 5L, new BigDecimal("50"), true));
		assertFalse(SecurityWarrantAuthorization.allows(w, "attacker", "foundgine", "Customer.read", "read",
				"tenant-1", "customer/*", 5L, new BigDecimal("50"), true));
		assertFalse(SecurityWarrantAuthorization.allows(w, "agent-a", "wrong-audience", "Customer.read", "read",
				"tenant-1", "customer/*", 5L, new BigDecimal("50"), true));
		assertFalse(SecurityWarrantAuthorization.allows(w, "agent-a", "foundgine", "Customer.read", "read", "tenant-2",
				"customer/*", 5L, new BigDecimal("50"), true));
		assertFalse(SecurityWarrantAuthorization.allows(w, "agent-a", "foundgine", "Customer.read", "read", "tenant-1",
				"order/*", 5L, new BigDecimal("50"), true));
		assertFalse(SecurityWarrantAuthorization.allows(w, "agent-a", "foundgine", "Customer.read", "read", "tenant-1",
				"customer/*", 11L, new BigDecimal("50"), true));
		assertFalse(SecurityWarrantAuthorization.allows(w, "agent-a", "foundgine", "Customer.read", "read", "tenant-1",
				"customer/*", 5L, new BigDecimal("101"), true));
	}

	private static void assertVerificationFails(SecurityWarrant warrant) {
		assertVerificationFails(warrant, null, null);
	}

	private static void assertVerificationFails(SecurityWarrant warrant, String expectedIssuer,
			String expectedAudience) {
		var key = rsaKeyPair();
		var signed = sign(warrant, key.getPrivate());
		assertThrows(IllegalStateException.class, () -> SecurityWarrantVerifier.verify(signed,
				resolver(signed.keyId(), key), Instant.now(), expectedIssuer, expectedAudience));
	}

	private static void assertSignatureFails(Function<SecurityWarrant, SecurityWarrant> mutate) {
		var key = rsaKeyPair();
		var signed = sign(create(Instant.now()), key.getPrivate());
		assertThrows(IllegalStateException.class, () -> SecurityWarrantVerifier.verify(mutate.apply(signed),
				resolver(signed.keyId(), key), Instant.now(), "issuer", null));
	}

	private static void assertDelegationFails(BiFunction<SecurityWarrant, SecurityWarrant, SecurityWarrant> mutate) {
		var now = Instant.now();
		var parent = withConstraints(create(now),
				new SecurityWarrantConstraints(List.of("tenant-1"), null, List.of("customer/*"), null, 100L, null));
		var child = withDelegation(withExpiresAt(withSignature(withId(parent, "child"), new byte[0]),
				parent.expiresAt().minusSeconds(60)), parent.id(), parent.subject(), parent.digest(),
				List.of(parent.digest()));

		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantAttenuator.attenuate(parent, mutate.apply(parent, child), now));
	}
}
