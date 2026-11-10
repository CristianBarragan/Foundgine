package com.foundgine.providers.gateway.approvals;

/** Supplies AES-256 keys by id so approval payload keys can be rotated. Key material never leaves the host's key store. */
public interface FoundgineGatewayApprovalKeyProvider {
    /** Id of the key used for new encryptions. Must match {@code [A-Za-z0-9._-]{1,64}}. */
    String currentKeyId();

    /** Returns the 32-byte key for {@code keyId}, or throws if the key is unknown or retired. */
    byte[] key(String keyId);
}
