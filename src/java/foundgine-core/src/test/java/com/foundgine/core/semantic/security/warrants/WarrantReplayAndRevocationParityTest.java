package com.foundgine.core.semantic.security.warrants;

import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

class WarrantReplayAndRevocationParityTest {
	private static SecurityWarrant warrant() {
		var now = Instant.now();
		return SecurityWarrant.ofDefaults("w-replay", "issuer", "agent", "audience", List.of(),
				new SecurityWarrantConstraints(), now.minusSeconds(5), now.plusSeconds(300), "nonce", "key", null,
				new byte[0]);
	}

	@Test
	void revocationInvalidatesPreviouslyValidWarrant() {
		var w = warrant();
		var store = new MemorySecurityWarrantRevocationStore();
		var now = Instant.now();

		SecurityWarrantRevocationGuard.validate(w, store, now);
		store.revoke(w, now);

		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantRevocationGuard.validate(w, store, Instant.now()));
	}

	@Test
	void revocationSnapshotDetectsAuthorityChange() {
		var w = warrant();
		var store = new MemorySecurityWarrantRevocationStore();
		var snapshot = SecurityWarrantRevocationSnapshot.capture(store);

		store.revoke(w, Instant.now());

		assertThrows(IllegalStateException.class, () -> snapshot.assertUnchanged(store));
	}

	@Test
	void replayGuardRejectsSecondConsumption() {
		var w = warrant();
		var store = new MemorySecurityWarrantReplayStore();

		SecurityWarrantReplayGuard.consume(w, store, Instant.now());
		assertThrows(IllegalStateException.class, () -> SecurityWarrantReplayGuard.consume(w, store, Instant.now()));
	}
}
