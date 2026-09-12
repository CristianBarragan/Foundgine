package com.foundgine.core.semantic.security.warrants;

import org.junit.jupiter.api.Test;
import java.time.Instant;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

/**
 * Parity coverage for delegation state transitions and fail-closed delegation.
 */
class SecurityWarrantDelegationStateMachineParityTest {
	private static SecurityWarrant warrant() {
		var now = Instant.now();
		return SecurityWarrant.ofDefaults("w-state", "issuer", "agent", "audience", List.of(),
				new SecurityWarrantConstraints(), now.minusSeconds(10), now.plusSeconds(300), "nonce-state",
				"key-state", null, new byte[0]);
	}

	@Test
	void registrationCreatesActiveSnapshot() {
		var w = warrant();
		var machine = new SecurityWarrantDelegationStateMachine();
		var snapshot = machine.register(w, "key-1");
		assertEquals(SecurityWarrantDelegationStateMachine.State.ACTIVE, snapshot.state());
		assertEquals(1, snapshot.sequence());
		assertEquals("key-1", snapshot.activeKeyId());
	}

	@Test
	void revokedWarrantCannotDelegate() {
		var w = warrant();
		var machine = new SecurityWarrantDelegationStateMachine();
		machine.register(w, "key-1");
		var transition = machine.revoke(w);
		assertEquals(SecurityWarrantDelegationStateMachine.State.REVOKED, transition.after().state());
		assertThrows(IllegalStateException.class, () -> machine.assertCanDelegate(w));
	}

	@Test
	void compromisedWarrantCannotReturnToActiveThroughKeyRotation() {
		var w = warrant();
		var machine = new SecurityWarrantDelegationStateMachine();
		machine.register(w, "key-1");
		machine.compromise(w);
		assertThrows(IllegalStateException.class, () -> machine.rotateKey(w, "key-2"));
		assertEquals(SecurityWarrantDelegationStateMachine.State.COMPROMISED, machine.read(w).state());
	}

	@Test
	void keyRotationAdvancesSequenceWithoutChangingAuthorityState() {
		var w = warrant();
		var machine = new SecurityWarrantDelegationStateMachine();
		machine.register(w, "key-1");
		var transition = machine.rotateKey(w, "key-2");
		assertEquals(SecurityWarrantDelegationStateMachine.State.ACTIVE, transition.after().state());
		assertEquals(2, transition.after().sequence());
		assertEquals("key-2", transition.after().activeKeyId());
	}

	@Test
	void duplicateRegistrationIsRejected() {
		var w = warrant();
		var machine = new SecurityWarrantDelegationStateMachine();
		machine.register(w, "key-1");
		assertThrows(IllegalStateException.class, () -> machine.register(w, "key-2"));
	}
}
