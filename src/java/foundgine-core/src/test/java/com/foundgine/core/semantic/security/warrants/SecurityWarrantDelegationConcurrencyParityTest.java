package com.foundgine.core.semantic.security.warrants;

import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of
 * {@code Foundgine.Core.Semantic.Tests.Security.Warrants.SecurityWarrantDelegationConcurrencySecurityTests}.
 */
class SecurityWarrantDelegationConcurrencyParityTest {

	private static SecurityWarrant create(Instant now) {
		return new SecurityWarrant("root", "root-issuer", "agent-a", "foundgine",
				List.of(new CapabilityGrant("Customer.read", "read", List.of("customer/*"))),
				new SecurityWarrantConstraints(List.of("tenant-1"), null, List.of("customer/*"), null, 100L, null),
				now.minusSeconds(60), now.plusSeconds(3600), "nonce-root", "key-root", null, new byte[0]);
	}

	private static SecurityWarrant child(SecurityWarrant parent, String id, String subject, String nonce) {
		var path = new ArrayList<>(parent.delegationPath());
		path.add(parent.digest());
		return new SecurityWarrant(id, parent.subject(), subject, parent.audience(), parent.grants(),
				parent.constraints(), parent.issuedAt(), parent.expiresAt().minusSeconds(60), nonce, parent.keyId(),
				parent.id(), new byte[0], parent.digest(), path);
	}

	@Test
	void concurrentChildrenFromSameParentRequireDistinctFreshParentSequences() {
		var now = Instant.now();
		var root = create(now);
		var store = new MemorySecurityWarrantDelegationConcurrencyStore();
		var snapshot = store.capture(root);
		var childA = child(root, "child-a", "agent-a", "nonce-a");
		var childB = child(root, "child-b", "agent-b", "nonce-b");

		store.commitChild(root, childA, snapshot);

		assertThrows(IllegalStateException.class, () -> store.commitChild(root, childB, snapshot));
		assertFalse(store.isCommitted(childB));

		var fresh = store.capture(root);
		store.commitChild(root, childB, fresh);
		assertTrue(store.isCommitted(childB));
	}

	@Test
	void sameChildIdentityCannotBeCommittedTwice() {
		var now = Instant.now();
		var root = create(now);
		var childNode = child(root, "child", "agent-b", "nonce-a");
		var store = new MemorySecurityWarrantDelegationConcurrencyStore();

		store.commitChild(root, childNode, store.capture(root));
		assertThrows(IllegalStateException.class,
				() -> store.commitChild(root, childNode, store.capture(root)));
	}

	@Test
	void sameNonceCannotForkTwoChildrenUnderOneParent() {
		var now = Instant.now();
		var root = create(now);
		var childA = child(root, "child-a", "agent-a", "same-nonce");
		var childB = child(root, "child-b", "agent-b", "same-nonce");
		var store = new MemorySecurityWarrantDelegationConcurrencyStore();

		store.commitChild(root, childA, store.capture(root));
		assertThrows(IllegalStateException.class, () -> store.commitChild(root, childB, store.capture(root)));
	}

	@Test
	void snapshotIsBoundToExactParentDigest() {
		var now = Instant.now();
		var root = create(now);
		var base = create(now);
		var other = new SecurityWarrant("other-root", base.issuer(), base.subject(), base.audience(), base.grants(),
				base.constraints(), base.issuedAt(), base.expiresAt(), base.nonce(), base.keyId(), base.parentId(),
				base.signature(), base.parentDigest(), base.delegationPath());
		var childNode = child(other, "child", "agent-b", "nonce");
		var store = new MemorySecurityWarrantDelegationConcurrencyStore();
		var snapshot = store.capture(root);

		assertThrows(IllegalStateException.class, () -> store.commitChild(other, childNode, snapshot));
	}

	@Test
	void failedStaleWriterDoesNotConsumeAChildSlot() {
		var now = Instant.now();
		var root = create(now);
		var store = new MemorySecurityWarrantDelegationConcurrencyStore();
		var stale = store.capture(root);
		var winner = child(root, "winner", "agent-a", "nonce-winner");
		var loser = child(root, "loser", "agent-b", "nonce-loser");

		store.commitChild(root, winner, stale);
		assertThrows(IllegalStateException.class, () -> store.commitChild(root, loser, stale));
		var fresh = store.capture(root);
		store.commitChild(root, loser, fresh);
		assertTrue(store.isCommitted(loser));
	}

	@Test
	void committedChildStillRequiresTheExistingDelegationAttenuationRules() {
		var now = Instant.now();
		var root = create(now);
		var base = child(root, "invalid", "agent-b", "nonce");
		var invalid = new SecurityWarrant(base.id(), base.issuer(), base.subject(), base.audience(), base.grants(),
				SecurityWarrantConstraints.UNRESTRICTED, base.issuedAt(), base.expiresAt(), base.nonce(),
				base.keyId(), base.parentId(), base.signature(), base.parentDigest(), base.delegationPath());
		var store = new MemorySecurityWarrantDelegationConcurrencyStore();

		assertThrows(IllegalStateException.class, () -> store.commitChild(root, invalid, store.capture(root)));
	}
}
