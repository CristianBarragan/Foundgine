package com.foundgine.providers.gateway.execution;

import java.time.Instant;

public record FoundgineGatewayExecutionStatus(String executionId, String status, String operationKind,
        Instant startedAt, Instant completedAt, String outcomeCode) { }
