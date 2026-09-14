package com.foundgine.runtime.controlplane;

import java.util.Objects;
import java.util.Set;

/**
 * Port of
 * {@code Foundgine.Runtime.ControlPlane.AuthorizationContextIntegrityKeyRingManager}.
 *
 * <p>
 * Process-local atomic owner of the current
 * {@link AuthorizationContextIntegrityKeyRing} snapshot. A rotation or
 * retirement publishes a complete immutable snapshot under one lock, so readers
 * never observe a partially rotated configuration.
 *
 * <p>
 * <b>Porting decision:</b> C#'s {@code lock (_gate)} over a plain
 * {@code object} is ported as a {@code synchronized} block over a private final
 * {@code Object} monitor — the direct Java analogue with the same
 * mutual-exclusion semantics, rather than introducing
 * {@code java.util.concurrent} lock machinery the original didn't use.
 */
public final class AuthorizationContextIntegrityKeyRingManager {
	private final Object gate = new Object();
	private final IAuthorizationKeyRotationAuthorizer authorizer;
	private AuthorizationContextIntegrityKeyRing current;

	public AuthorizationContextIntegrityKeyRingManager(AuthorizationContextIntegrityKeyRing initialRing,
			IAuthorizationKeyRotationAuthorizer authorizer) {
		this.current = Objects.requireNonNull(initialRing, "initialRing");
		this.authorizer = Objects.requireNonNull(authorizer, "authorizer");
	}

	public AuthorizationContextIntegrityKeyRingManager(AuthorizationContextIntegrityKeyRing initialRing) {
		this(initialRing, new RejectAllAuthorizationKeyRotationAuthorizer());
	}

	public AuthorizationContextIntegrityKeyRing snapshot() {
		synchronized (gate) {
			return current;
		}
	}

	public AuthorizationContextIntegrityKeyRing rotate(AuthorizationContextIntegrityKey newActiveKey,
			AuthorizationKeyRotationProvenance provenance) {
		synchronized (gate) {
			if (!authorizer.isAuthorized(provenance)) {
				throw new SecurityException("Key rotation provenance is not authorized.");
			}
			current = current.rotate(newActiveKey, provenance);
			return current;
		}
	}

	public AuthorizationContextIntegrityKeyRing retire(String keyId, AuthorizationKeyRotationProvenance provenance,
			Set<String> persistedKeyIds) {
		synchronized (gate) {
			if (!authorizer.isAuthorized(provenance)) {
				throw new SecurityException("Key lifecycle provenance is not authorized.");
			}
			current = current.retire(keyId, provenance, persistedKeyIds);
			return current;
		}
	}
}
