package com.foundgine.runtime.controlplane;

/**
 * Port of
 * {@code Foundgine.Runtime.ControlPlane.AuthorizationIntegrityKeyState}.
 *
 * <p>
 * Key ring lifecycle state for authorization-context integrity. The active key
 * is used for new writes; all configured keys may verify existing records
 * during rotation.
 */
public enum AuthorizationIntegrityKeyState {
	ACTIVE, VERIFICATION_ONLY, RETIRED,
}
