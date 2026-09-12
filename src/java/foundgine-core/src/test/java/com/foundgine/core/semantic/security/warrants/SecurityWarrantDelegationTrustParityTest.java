package com.foundgine.core.semantic.security.warrants;

import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.Arrays;
import java.util.List;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertThrows;

/**
 * Port of
 * {@code Foundgine.Core.Semantic.Tests.Security.Warrants.SecurityWarrantDelegationTrustSecurityTests}.
 */
class SecurityWarrantDelegationTrustParityTest {

	private static ISecurityWarrantDelegationTrustResolver resolver(DelegationIssuerTrust... trusts) {
		return issuer -> Arrays.stream(trusts).filter(x -> x.issuer().equals(issuer)).findFirst().orElse(null);
	}

	private static SecurityWarrant create(String id, String issuer, String subject) {
		return create(id, issuer, subject, "issuer-key", null);
	}

	private static SecurityWarrant create(String id, String issuer, String subject, String keyId, Instant now) {
		var t = now != null ? now : Instant.now();
		return new SecurityWarrant(id, issuer, subject, "api",
				List.of(new CapabilityGrant("Customer.read", "read", List.of("customer/*"))),
				new SecurityWarrantConstraints(List.of("tenant-a"), null, null, List.of("read"), null, null),
				t.minusSeconds(60), t.plusSeconds(600), "nonce-" + id, keyId, null, new byte[0]);
	}

	private static SecurityWarrant child(SecurityWarrant parent) {
		return child(parent, "service-b", "child-key");
	}

	private static SecurityWarrant child(SecurityWarrant parent, String subject, String keyId) {
		return new SecurityWarrant("child", parent.subject(), subject, parent.audience(), parent.grants(),
				parent.constraints(), parent.issuedAt(), parent.expiresAt().minusSeconds(60), parent.nonce(), keyId,
				parent.id(), new byte[0], parent.digest(), List.of(parent.digest()));
	}

	@Test
	void nonDelegatingIssuerIsRejected() {
		var now = Instant.now();
		var parent = create("p", "root", "service-a");
		var child = child(parent);
		var trust = resolver(new DelegationIssuerTrust("service-a", Set.of("child-key"), false));

		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationTrust.verifyIssuer(parent, child, trust, now, "tenant-a"));
	}

	@Test
	void unknownIssuerIsRejected() {
		var now = Instant.now();
		var parent = create("p", "root", "service-a");
		var child = child(parent);

		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationTrust.verifyIssuer(parent, child, resolver(), now, "tenant-a"));
	}

	@Test
	void keySubstitutionIsRejected() {
		var now = Instant.now();
		var parent = create("p", "root", "service-a");
		var child = child(parent, "service-b", "forged-key");
		var trust = resolver(new DelegationIssuerTrust("service-a", Set.of("child-key"), true));

		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationTrust.verifyIssuer(parent, child, trust, now, "tenant-a"));
	}

	@Test
	void executeOnlyIssuerCannotDelegate() {
		var now = Instant.now();
		var parent = create("p", "root", "service-a");
		var child = child(parent);
		var trust = resolver(new DelegationIssuerTrust("service-a", Set.of("child-key"), false));

		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationTrust.verifyIssuer(parent, child, trust, now, null));
	}

	@Test
	void trustedIssuerWithActiveDelegationKeyIsAccepted() {
		var now = Instant.now();
		var parent = create("p", "root", "service-a");
		var child = child(parent);
		var trust = resolver(new DelegationIssuerTrust("service-a", Set.of("child-key"), true));

		assertDoesNotThrow(() -> SecurityWarrantDelegationTrust.verifyIssuer(parent, child, trust, now, "tenant-a"));
	}

	@Test
	void audienceScopeIsEnforced() {
		var now = Instant.now();
		var parent = create("p", "root", "service-a");
		var base = child(parent);
		var withOtherAudience = new SecurityWarrant(base.id(), base.issuer(), base.subject(), "other-api",
				base.grants(), base.constraints(), base.issuedAt(), base.expiresAt(), base.nonce(), base.keyId(),
				base.parentId(), base.signature(), base.parentDigest(), base.delegationPath());
		var trust = resolver(new DelegationIssuerTrust("service-a", Set.of("child-key"), true, "api", Set.of(),
				java.util.Map.of()));

		assertThrows(IllegalStateException.class, () -> SecurityWarrantDelegationTrust.verifyIssuer(parent,
				withOtherAudience, trust, now, null));
	}

	@Test
	void tenantScopeIsEnforced() {
		var now = Instant.now();
		var parent = create("p", "root", "service-a");
		var child = child(parent);
		var trust = resolver(new DelegationIssuerTrust("service-a", Set.of("child-key"), true, null,
				Set.of("tenant-b"), java.util.Map.of()));

		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationTrust.verifyIssuer(parent, child, trust, now, "tenant-a"));
	}

	@Test
	void issuerMustBeParentSubject() {
		var now = Instant.now();
		var parent = create("p", "root", "service-a");
		var base = child(parent);
		var withOtherIssuer = new SecurityWarrant(base.id(), "service-c", base.subject(), base.audience(),
				base.grants(), base.constraints(), base.issuedAt(), base.expiresAt(), base.nonce(), base.keyId(),
				base.parentId(), base.signature(), base.parentDigest(), base.delegationPath());
		var trust = resolver(new DelegationIssuerTrust("service-c", Set.of("child-key"), true));

		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationTrust.verifyIssuer(parent, withOtherIssuer, trust, now, null));
	}
}
