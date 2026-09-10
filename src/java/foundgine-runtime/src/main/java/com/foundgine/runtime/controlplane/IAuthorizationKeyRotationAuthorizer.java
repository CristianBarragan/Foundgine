package com.foundgine.runtime.controlplane;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.IAuthorizationKeyRotationAuthorizer}.
 *
 * <p>Process-local atomic owner of the current key-ring snapshot. A
 * rotation publishes a complete immutable snapshot under one lock, so
 * readers never observe a partially rotated configuration.
 */
public interface IAuthorizationKeyRotationAuthorizer {
    boolean isAuthorized(AuthorizationKeyRotationProvenance provenance);
}
