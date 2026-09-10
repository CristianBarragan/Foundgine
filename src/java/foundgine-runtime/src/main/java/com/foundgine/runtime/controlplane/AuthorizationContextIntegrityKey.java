package com.foundgine.runtime.controlplane;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.AuthorizationContextIntegrityKey}.
 *
 * <p>External key material used to authenticate persisted authorization
 * context. Key bytes must never be persisted in PostgreSQL.
 *
 * <p><b>Porting decision:</b> although the C# file lives under
 * {@code ControlPlane/Recovery/}, its declared namespace is
 * {@code Foundgine.Runtime.ControlPlane} (no {@code .Recovery} segment) —
 * this port follows the namespace, not the directory, so the type lands in
 * {@code com.foundgine.runtime.controlplane} alongside {@code ToolCallGovernor}.
 * The constructor and {@link #key()} accessor both defensively copy the key
 * bytes (the C# property returns the same array reference on every access);
 * the extra copy is cheap relative to a 32+ byte key and closes off external
 * mutation of key material through a returned reference.
 */
public final class AuthorizationContextIntegrityKey {
    private final String keyId;
    private final byte[] key;

    public AuthorizationContextIntegrityKey(String keyId, byte[] key) {
        if (keyId == null || keyId.isBlank()) {
            throw new IllegalArgumentException("Integrity key id is required.");
        }
        if (key == null || key.length < 32) {
            throw new IllegalArgumentException("Integrity keys must contain at least 32 bytes.");
        }

        this.keyId = keyId;
        this.key = key.clone();
    }

    public String keyId() {
        return keyId;
    }

    public byte[] key() {
        return key.clone();
    }
}
