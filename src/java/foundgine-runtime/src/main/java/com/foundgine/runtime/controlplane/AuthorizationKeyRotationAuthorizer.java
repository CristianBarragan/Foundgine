package com.foundgine.runtime.controlplane;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Map;
import java.util.Objects;
import java.util.UUID;

/**
 * Port of
 * {@code Foundgine.Runtime.ControlPlane.AuthorizationKeyRotationAuthorizer}.
 *
 * <p>
 * External key-lifecycle authority. Operator credentials are represented only
 * by their fingerprints; credential material is never persisted in PostgreSQL.
 *
 * <p>
 * <b>Porting decision:</b> {@code CryptographicOperations.FixedTimeEquals} over
 * the UTF-8 bytes of the two fingerprints is ported as
 * {@link MessageDigest#isEqual} for the same constant-time guarantee used in
 * {@link AuthorizationContextIntegrityKeyRing}.
 */
public final class AuthorizationKeyRotationAuthorizer implements IAuthorizationKeyRotationAuthorizer {
	private final Map<UUID, String> operators;

	public AuthorizationKeyRotationAuthorizer(Map<UUID, String> operators) {
		Objects.requireNonNull(operators, "operators");
		if (operators.isEmpty()) {
			throw new IllegalArgumentException("At least one authorized key-rotation operator is required.");
		}
		this.operators = Map.copyOf(operators);
	}

	@Override
	public boolean isAuthorized(AuthorizationKeyRotationProvenance provenance) {
		if (provenance.operatorId().equals(AuthorizationKeyRotationProvenance.EMPTY_OPERATOR_ID)) {
			return false;
		}
		var expected = operators.get(provenance.operatorId());
		if (expected == null) {
			return false;
		}
		return MessageDigest.isEqual(expected.getBytes(StandardCharsets.UTF_8),
				provenance.credentialFingerprint().getBytes(StandardCharsets.UTF_8));
	}
}
