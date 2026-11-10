package com.foundgine.providers.gateway.approvals;

import com.foundgine.runtime.MutationPlanApproval;
import java.util.Objects;

/**
 * Encrypts approval payloads at rest (AES-256-GCM, see {@link ApprovalPayloadEnvelope}) by decorating the host codec that
 * supplies the AOT/native-image-safe serialization; no reflection-based serializer is chosen here. Keys are addressed by
 * id so they can be rotated while older approvals stay readable until they expire. Evidence is non-secret and passes
 * through unencrypted.
 *
 * <p>Limit: the codec port does not receive the row's subject, tenant or digest, so the ciphertext is not bound to them
 * cryptographically; the store's lookup predicates enforce that binding.
 */
public final class EncryptingFoundgineGatewayApprovalPayloadCodec implements FoundgineGatewayApprovalPayloadCodec {
    private final FoundgineGatewayApprovalPayloadCodec inner;
    private final FoundgineGatewayApprovalKeyProvider keys;

    public EncryptingFoundgineGatewayApprovalPayloadCodec(FoundgineGatewayApprovalPayloadCodec inner,
            FoundgineGatewayApprovalKeyProvider keys) {
        this.inner = Objects.requireNonNull(inner, "inner");
        this.keys = Objects.requireNonNull(keys, "keys");
    }

    @Override public String encodeApproval(MutationPlanApproval approval) {
        String keyId = keys.currentKeyId();
        ApprovalPayloadEnvelope.requireKeyId(keyId);
        return ApprovalPayloadEnvelope.seal(inner.encodeApproval(approval), keyId, keys.key(keyId));
    }

    @Override public MutationPlanApproval decodeApproval(String payload) {
        return inner.decodeApproval(ApprovalPayloadEnvelope.open(payload, keys));
    }

    @Override public String encodeEvidence(FoundgineGatewayApprovalEvidence evidence) { return inner.encodeEvidence(evidence); }
}
