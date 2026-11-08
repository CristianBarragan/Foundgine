package com.foundgine.extensions.temporal.activities;

import java.util.Map;

/** Context given to an operation. The idempotency key is stable across retries of the same activity. */
public record FoundgineOperationContext(String tenantId, String initiator, String correlationId, String payloadJson,
        Map<String, String> outputs, String idempotencyKey, boolean compensation, int attempt) { }
