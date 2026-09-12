package com.foundgine.core.semantic.security.warrants;

import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of
 * {@code Foundgine.Core.Semantic.Tests.Security.Warrants.SecurityWarrantDelegationCompromiseSecurityTests}.
 */
class SecurityWarrantDelegationCompromiseParityTest {

	private static SecurityWarrant create(Instant now) {
		return new SecurityWarrant("root", "root-issuer", "agent-a", "foundgine",
				List.of(new CapabilityGrant("Customer.read", "read", List.of("customer/*"))),
				new SecurityWarrantConstraints(List.of("tenant-1"), null, List.of("customer/*"), null, 100L, null),
				now.minusSeconds(60), now.plusSeconds(3600), "nonce-root", "key-root", null, new byte[0]);
	}

	private static SecurityWarrant child(SecurityWarrant parent, String id, String subject) {
		var path = new java.util.ArrayList<>(parent.delegationPath());
		path.add(parent.digest());
		return new SecurityWarrant(id, parent.subject(), subject, parent.audience(), parent.grants(),
				parent.constraints(), parent.issuedAt(), parent.expiresAt().minusSeconds(60), parent.nonce(),
				parent.keyId(), parent.id(), new byte[0], parent.digest(), path);
	}

	@Test
	void compromisingAnIntermediateNodeInvalidatesOnlyItsDescendants() {
		var now = Instant.now();
		var root = create(now);
		var childA = child(root, "child-a", "agent-a");
		var childB = child(root, "child-b", "agent-b");
		var grandchildA = child(childA, "grandchild-a", "agent-c");
		var store = new MemorySecurityWarrantDelegationCompromiseStore();

		store.compromise(childA, now, childA.issuer(), null);

		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationCompromiseGuard.validate(childA, store, now));
		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationCompromiseGuard.validate(grandchildA, store, now));
		SecurityWarrantDelegationCompromiseGuard.validate(childB, store, now);
	}

	@Test
	void compromisingRootInvalidatesTheEntireDelegationSubtree() {
		var now = Instant.now();
		var root = create(now);
		var childNode = child(root, "child", "agent-b");
		var grandchild = child(childNode, "grandchild", "agent-c");
		var store = new MemorySecurityWarrantDelegationCompromiseStore();

		store.compromise(root, now, null, root.keyId());

		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationCompromiseGuard.validate(root, store, now));
		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationCompromiseGuard.validate(childNode, store, now));
		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationCompromiseGuard.validate(grandchild, store, now));
	}

	@Test
	void unrelatedSiblingBranchSurvivesIntermediateCompromise() {
		var now = Instant.now();
		var root = create(now);
		var childA = child(root, "child-a", "agent-a");
		var childB = child(root, "child-b", "agent-b");
		var grandchildB = child(childB, "grandchild-b", "agent-c");
		var store = new MemorySecurityWarrantDelegationCompromiseStore();

		store.compromise(childA, now, null, childA.keyId());

		SecurityWarrantDelegationCompromiseGuard.validate(childB, store, now);
		SecurityWarrantDelegationCompromiseGuard.validate(grandchildB, store, now);
	}

	@Test
	void compromisedKeyIsPathBoundAndDoesNotRevokeUnrelatedKey() {
		var now = Instant.now();
		var root = create(now);
		var childA = withKeyId(child(root, "child-a", "agent-a"), "key-a");
		var childB = withKeyId(child(root, "child-b", "agent-b"), "key-b");
		var store = new MemorySecurityWarrantDelegationCompromiseStore();

		store.compromise(childA, now, null, childA.keyId());

		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationCompromiseGuard.validate(childA, store, now));
		SecurityWarrantDelegationCompromiseGuard.validate(childB, store, now);
	}

	@Test
	void compromiseStateChangeIsDetectedAtFinalExecutionGate() {
		var now = Instant.now();
		var root = create(now);
		var childNode = child(root, "child", "agent-b");
		var store = new MemorySecurityWarrantDelegationCompromiseStore();
		SecurityWarrantDelegationCompromiseGuard.validate(childNode, store, now);
		var snapshot = SecurityWarrantDelegationCompromiseGuard.capture(store);

		store.compromise(childNode, now, childNode.issuer(), null);

		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationCompromiseGuard.assertUnchanged(store, snapshot));
	}

	@Test
	void repeatedCompromiseIsMonotonic() {
		var now = Instant.now();
		var root = create(now);
		var store = new MemorySecurityWarrantDelegationCompromiseStore();
		var first = store.compromise(root, now, null, root.keyId());
		var second = store.compromise(root, now.plusSeconds(1), null, root.keyId());

		assertEquals(first, second);
		assertTrue(store.currentSequence() >= second.sequence());
		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationCompromiseGuard.validate(root, store, now));
	}

	private static SecurityWarrant withKeyId(SecurityWarrant w, String keyId) {
		return new SecurityWarrant(w.id(), w.issuer(), w.subject(), w.audience(), w.grants(), w.constraints(),
				w.issuedAt(), w.expiresAt(), w.nonce(), keyId, w.parentId(), w.signature(), w.parentDigest(),
				w.delegationPath());
	}
}
