package com.foundgine.extensions.temporal.hosting;

import java.util.UUID;

public record FoundgineWorkflowStartRequest(String tenantId, String initiator, String process, String businessKey,
        String payloadJson, String correlationId, UUID sourceMessageId) {
    public FoundgineWorkflowStartRequest(String tenantId, String initiator, String process, String businessKey,
            String payloadJson) {
        this(tenantId, initiator, process, businessKey, payloadJson, null, null);
    }
}
