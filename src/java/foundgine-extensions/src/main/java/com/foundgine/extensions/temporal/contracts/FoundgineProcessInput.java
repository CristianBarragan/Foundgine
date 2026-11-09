package com.foundgine.extensions.temporal.contracts;

import java.util.UUID;

/** Workflow input. Only identifiers travel here: workflow history is persisted, so never put warrants or tokens in it. */
public record FoundgineProcessInput(String tenantId, String initiator, String businessKey, String payloadJson,
        FoundgineProcessDefinition definition, FoundgineActivityPolicy activityPolicy, String correlationId,
        UUID sourceMessageId) { }
