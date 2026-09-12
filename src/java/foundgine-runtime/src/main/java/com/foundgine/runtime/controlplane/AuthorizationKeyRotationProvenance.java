package com.foundgine.runtime.controlplane;

import java.util.UUID;

/**
 * Port of
 * {@code Foundgine.Runtime.ControlPlane.AuthorizationKeyRotationProvenance}.
 *
 * <p>
 * <b>Porting decision:</b> {@code Guid} is ported as {@link UUID}; C#'s
 * {@code Guid.Empty} sentinel is ported as the all-zero UUID
 * {@code new UUID(0L, 0L)}.
 */
public record AuthorizationKeyRotationProvenance(UUID operatorId, long rotationSequence, String credentialFingerprint) {

	public static final UUID EMPTY_OPERATOR_ID = new UUID(0L, 0L);
}
