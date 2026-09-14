package com.foundgine.runtime.controlplane;

/**
 * Port of
 * {@code Foundgine.Runtime.ControlPlane.AuthorizationIntegrityKeyDescriptor}.
 */
public record AuthorizationIntegrityKeyDescriptor(String keyId, AuthorizationIntegrityKeyState state) {
}
