package com.foundgine.core.semantic.security.execution;

/**
 * Host-owned source of the caller's security context for a single request.
 * Transport adapters must obtain this from trusted host authentication state,
 * never from the untrusted request payload.
 */
@FunctionalInterface
public interface ISecurityExecutionContextProvider {
    /** Returns the current trusted context, or {@code null} when none exists. */
    SecurityExecutionContext getSecurityExecutionContext();
}
