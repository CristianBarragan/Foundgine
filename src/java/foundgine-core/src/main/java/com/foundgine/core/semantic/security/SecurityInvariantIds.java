package com.foundgine.core.semantic.security;

/**
 * Port of {@code Foundgine.Core.Semantic.Security.SecurityInvariantIds}:
 * canonical identifiers for Foundgine security invariants.
 */
public final class SecurityInvariantIds {

	public static final String AUTHORIZATION_REQUIRED = "authorization.required";
	public static final String RUNTIME_AUTHORIZATION = "authorization.runtime";
	public static final String AUTHORIZATION_OWNERSHIP = "authorization.ownership";
	public static final String TENANT_ISOLATION = "tenant.isolation";
	public static final String FIELD_VISIBILITY = "visibility.field";
	public static final String RELATIONSHIP_VISIBILITY = "visibility.relationship";
	public static final String PARAMETERIZED_VALUES = "execution.parameterized-values";
	public static final String PLAN_CACHE_CONTEXT_ISOLATION = "planning.cache-context-isolation";
	public static final String ATOMIC_MUTATION = "mutation.atomic";
	public static final String MUTATION_ROW_LOCKING = "mutation.atomic.row-locking";
	public static final String IDEMPOTENCY = "mutation.idempotency";
	public static final String REPLAY_PROTECTION = "mutation.replay-protection";
	public static final String AUDIT_REQUIRED = "evidence.audit";
	public static final String EXECUTION_EVIDENCE_REQUIRED = "evidence.execution-receipt";
	public static final String TRANSACTION_READ_COMMITTED_ISOLATION = "mutation.transaction.read-committed-isolation";
	public static final String MUTATION_DAILY_LIMIT = "mutation.daily-limit";

	private SecurityInvariantIds() {
	}
}
