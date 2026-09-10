package com.foundgine.runtime.controlplane;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.RejectAllAuthorizationKeyRotationAuthorizer}.
 *
 * <p>Fail-closed default authorizer: every rotation/retirement request is
 * denied. Used as the implicit authorizer when
 * {@link AuthorizationContextIntegrityKeyRingManager} is constructed
 * without an explicit one, so key-ring mutation is opt-in rather than
 * silently permitted.
 */
public final class RejectAllAuthorizationKeyRotationAuthorizer implements IAuthorizationKeyRotationAuthorizer {
    @Override
    public boolean isAuthorized(AuthorizationKeyRotationProvenance provenance) {
        return false;
    }
}
