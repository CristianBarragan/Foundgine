package com.foundgine.core.execution.security;

import com.foundgine.core.semantic.security.SecurityInvariantIds;

import java.util.List;

/** Baseline profiles for providers shipped with Foundgine. */
public final class FoundgineProviderSecurityProfiles {
	private FoundgineProviderSecurityProfiles() {
	}

	public static ProviderSecurityConformanceProfile inMemory() {
		return new ProviderSecurityConformanceProfile("in-memory",
				List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED, SecurityInvariantIds.RUNTIME_AUTHORIZATION,
						SecurityInvariantIds.TENANT_ISOLATION, SecurityInvariantIds.FIELD_VISIBILITY,
						SecurityInvariantIds.RELATIONSHIP_VISIBILITY, SecurityInvariantIds.PARAMETERIZED_VALUES,
						SecurityInvariantIds.PLAN_CACHE_CONTEXT_ISOLATION),
				List.of("Suitable for semantic/query security testing; consequential PostgreSQL transaction guarantees are not inferred."));
	}

	public static ProviderSecurityConformanceProfile sql() {
		return new ProviderSecurityConformanceProfile("sql",
				List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED, SecurityInvariantIds.RUNTIME_AUTHORIZATION,
						SecurityInvariantIds.FIELD_VISIBILITY, SecurityInvariantIds.RELATIONSHIP_VISIBILITY,
						SecurityInvariantIds.PARAMETERIZED_VALUES, SecurityInvariantIds.PLAN_CACHE_CONTEXT_ISOLATION),
				List.of("Generic SQL preserves query-level guarantees; mutation guarantees require a provider-specific execution contract."));
	}

	public static ProviderSecurityConformanceProfile postgresTransferFunds() {
		return new ProviderSecurityConformanceProfile("postgres-transfer-funds",
				List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED, SecurityInvariantIds.RUNTIME_AUTHORIZATION,
						SecurityInvariantIds.TENANT_ISOLATION, SecurityInvariantIds.PARAMETERIZED_VALUES,
						SecurityInvariantIds.PLAN_CACHE_CONTEXT_ISOLATION, SecurityInvariantIds.ATOMIC_MUTATION,
						SecurityInvariantIds.MUTATION_ROW_LOCKING, SecurityInvariantIds.AUTHORIZATION_OWNERSHIP,
						SecurityInvariantIds.MUTATION_DAILY_LIMIT, SecurityInvariantIds.IDEMPOTENCY,
						SecurityInvariantIds.REPLAY_PROTECTION, SecurityInvariantIds.AUDIT_REQUIRED,
						SecurityInvariantIds.EXECUTION_EVIDENCE_REQUIRED,
						SecurityInvariantIds.TRANSACTION_READ_COMMITTED_ISOLATION),
				List.of("High-assurance TransferFunds provider; transaction and concurrency guarantees are backed by PostgreSQL integration tests."));
	}
}
