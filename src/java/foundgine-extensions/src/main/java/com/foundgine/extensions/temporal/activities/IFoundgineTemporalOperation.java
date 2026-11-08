package com.foundgine.extensions.temporal.activities;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.extensions.temporal.contracts.FoundgineOperationResult;

/**
 * A unit of business work. Operations run through Foundgine's authorization boundary using the activity's security
 * context. They MUST be idempotent: Temporal delivers activities at-least-once (use
 * {@link FoundgineOperationContext#idempotencyKey()}).
 */
public interface IFoundgineTemporalOperation {
    String name();

    FoundgineOperationResult execute(FoundgineOperationContext context, CancellationToken token) throws Exception;
}
