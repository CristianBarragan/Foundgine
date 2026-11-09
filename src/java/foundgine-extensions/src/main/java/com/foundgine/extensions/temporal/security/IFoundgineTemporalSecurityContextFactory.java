package com.foundgine.extensions.temporal.security;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;

/**
 * Host-owned. Builds the trusted security context an activity runs under. Workflow history is persisted and readable
 * by operators, so warrants and tokens are never carried in workflow input: only tenant and initiator identifiers are,
 * and the worker re-derives authority here on every activity attempt.
 */
@FunctionalInterface
public interface IFoundgineTemporalSecurityContextFactory {
    SecurityExecutionContext create(String tenantId, String initiator, CancellationToken cancellationToken);
}
