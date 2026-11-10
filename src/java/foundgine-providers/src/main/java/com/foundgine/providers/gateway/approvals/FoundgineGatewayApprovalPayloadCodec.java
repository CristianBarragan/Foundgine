package com.foundgine.providers.gateway.approvals;

import com.foundgine.runtime.MutationPlanApproval;

/** Host-supplied AOT/native-image-friendly codec. Implementations should encrypt approval payloads at rest. */
public interface FoundgineGatewayApprovalPayloadCodec {
    String encodeApproval(MutationPlanApproval approval);
    MutationPlanApproval decodeApproval(String payload);
    String encodeEvidence(FoundgineGatewayApprovalEvidence evidence);
}
