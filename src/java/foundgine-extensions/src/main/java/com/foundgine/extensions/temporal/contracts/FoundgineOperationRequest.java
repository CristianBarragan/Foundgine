package com.foundgine.extensions.temporal.contracts;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;

public record FoundgineOperationRequest(String operation, String tenantId, String initiator, String correlationId,
        String payloadJson, Map<String, String> outputs, boolean compensation) {
    public FoundgineOperationRequest {
        // snapshot: the workflow keeps mutating its outputs map after the activity was scheduled
        outputs = Collections.unmodifiableMap(new LinkedHashMap<>(outputs == null ? Map.of() : outputs));
    }
}
