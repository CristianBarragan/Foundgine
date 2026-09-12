package com.foundgine.core.semantic.security.warrants;

import org.junit.jupiter.api.Test;
import java.time.Instant;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

/**
 * Parity coverage for immutable delegation-chain ancestry and deterministic
 * chain digests.
 */
class SecurityWarrantDelegationChainParityTest {
	private static SecurityWarrant root() {
		var now = Instant.now();
		return SecurityWarrant.ofDefaults("root", "issuer", "subject", "audience", List.of(),
				new SecurityWarrantConstraints(), now.minusSeconds(10), now.plusSeconds(300), "root-nonce", "root-key",
				null, new byte[0]);
	}

	@Test
	void singleRootChainIsValid() {
		var chain = List.of(root());
		assertDoesNotThrow(() -> SecurityWarrantDelegationChainValidator.validate(chain, Instant.now()));
		assertNotNull(SecurityWarrantDelegationChainValidator.chainDigest(chain));
	}

	@Test
	void expectedRootDigestMismatchFailsClosed() {
		var chain = List.of(root());
		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationChainValidator.validate(chain, Instant.now(), "00"));
	}

	@Test
	void emptyChainIsRejected() {
		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationChainValidator.validate(List.of(), Instant.now()));
	}

	@Test
	void rootWithDelegationAncestryIsRejected() {
		var w = root();
		var tampered = SecurityWarrant.ofDefaults(w.id(), w.issuer(), w.subject(), w.audience(), w.grants(),
				w.constraints(), w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), "parent", w.signature());
		assertThrows(IllegalStateException.class,
				() -> SecurityWarrantDelegationChainValidator.validate(List.of(tampered), Instant.now()));
	}

	@Test
	void chainDigestIsDeterministic() {
		var chain = List.of(root());
		assertEquals(SecurityWarrantDelegationChainValidator.chainDigest(chain),
				SecurityWarrantDelegationChainValidator.chainDigest(chain));
	}
}
