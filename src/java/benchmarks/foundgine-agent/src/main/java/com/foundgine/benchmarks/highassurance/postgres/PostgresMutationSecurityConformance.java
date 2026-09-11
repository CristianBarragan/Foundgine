package com.foundgine.benchmarks.highassurance.postgres;

import com.foundgine.core.semantic.security.SecurityInvariantIds;

import java.util.ArrayList;
import java.util.List;

/**
 * Explicit high-assurance contract for the PostgreSQL TransferFunds fixture.
 * A declaration is evidence only when every required invariant is satisfied;
 * missing obligations fail closed.
 */
public record PostgresMutationSecurityConformance(
        boolean usesSingleTransaction,
        boolean locksMutationRowsDeterministically,
        boolean revalidatesAuthorizationAtExecution,
        boolean serializesIdempotencyKeys,
        boolean persistsIdempotencyInsideTransaction,
        boolean persistsAuditInsideTransaction,
        boolean emitsExecutionReceipt,
        boolean enforcesOwnership,
        boolean enforcesDailyLimit,
        boolean enforcesTenantIsolation,
        boolean enforcesReplayProtection) {

    public static final PostgresMutationSecurityConformance TRANSFER_FUNDS =
            new PostgresMutationSecurityConformance(true, true, true, true, true, true, true,
                    true, true, true, true);

    public PostgresMutationSecurityConformance withSingleTransaction(boolean v) { return copy(v, locksMutationRowsDeterministically, revalidatesAuthorizationAtExecution, serializesIdempotencyKeys, persistsIdempotencyInsideTransaction, persistsAuditInsideTransaction, emitsExecutionReceipt, enforcesOwnership, enforcesDailyLimit, enforcesTenantIsolation, enforcesReplayProtection); }
    public PostgresMutationSecurityConformance withLocks(boolean v) { return copy(usesSingleTransaction, v, revalidatesAuthorizationAtExecution, serializesIdempotencyKeys, persistsIdempotencyInsideTransaction, persistsAuditInsideTransaction, emitsExecutionReceipt, enforcesOwnership, enforcesDailyLimit, enforcesTenantIsolation, enforcesReplayProtection); }
    public PostgresMutationSecurityConformance withRuntimeAuthorization(boolean v) { return copy(usesSingleTransaction, locksMutationRowsDeterministically, v, serializesIdempotencyKeys, persistsIdempotencyInsideTransaction, persistsAuditInsideTransaction, emitsExecutionReceipt, enforcesOwnership, enforcesDailyLimit, enforcesTenantIsolation, enforcesReplayProtection); }
    public PostgresMutationSecurityConformance withIdempotency(boolean v) { return copy(usesSingleTransaction, locksMutationRowsDeterministically, revalidatesAuthorizationAtExecution, v, v, persistsAuditInsideTransaction, emitsExecutionReceipt, enforcesOwnership, enforcesDailyLimit, enforcesTenantIsolation, enforcesReplayProtection); }
    public PostgresMutationSecurityConformance withReplay(boolean v) { return copy(usesSingleTransaction, locksMutationRowsDeterministically, revalidatesAuthorizationAtExecution, serializesIdempotencyKeys, persistsIdempotencyInsideTransaction, persistsAuditInsideTransaction, emitsExecutionReceipt, enforcesOwnership, enforcesDailyLimit, enforcesTenantIsolation, v); }
    public PostgresMutationSecurityConformance withAudit(boolean v) { return copy(usesSingleTransaction, locksMutationRowsDeterministically, revalidatesAuthorizationAtExecution, serializesIdempotencyKeys, persistsIdempotencyInsideTransaction, v, emitsExecutionReceipt, enforcesOwnership, enforcesDailyLimit, enforcesTenantIsolation, enforcesReplayProtection); }
    public PostgresMutationSecurityConformance withReceipt(boolean v) { return copy(usesSingleTransaction, locksMutationRowsDeterministically, revalidatesAuthorizationAtExecution, serializesIdempotencyKeys, persistsIdempotencyInsideTransaction, persistsAuditInsideTransaction, v, enforcesOwnership, enforcesDailyLimit, enforcesTenantIsolation, enforcesReplayProtection); }
    public PostgresMutationSecurityConformance withOwnership(boolean v) { return copy(usesSingleTransaction, locksMutationRowsDeterministically, revalidatesAuthorizationAtExecution, serializesIdempotencyKeys, persistsIdempotencyInsideTransaction, persistsAuditInsideTransaction, emitsExecutionReceipt, v, enforcesDailyLimit, enforcesTenantIsolation, enforcesReplayProtection); }
    public PostgresMutationSecurityConformance withDailyLimit(boolean v) { return copy(usesSingleTransaction, locksMutationRowsDeterministically, revalidatesAuthorizationAtExecution, serializesIdempotencyKeys, persistsIdempotencyInsideTransaction, persistsAuditInsideTransaction, emitsExecutionReceipt, enforcesOwnership, v, enforcesTenantIsolation, enforcesReplayProtection); }
    public PostgresMutationSecurityConformance withTenantIsolation(boolean v) { return copy(usesSingleTransaction, locksMutationRowsDeterministically, revalidatesAuthorizationAtExecution, serializesIdempotencyKeys, persistsIdempotencyInsideTransaction, persistsAuditInsideTransaction, emitsExecutionReceipt, enforcesOwnership, enforcesDailyLimit, v, enforcesReplayProtection); }
    private PostgresMutationSecurityConformance copy(boolean a, boolean b, boolean c, boolean d, boolean e, boolean f, boolean g, boolean h, boolean i, boolean j, boolean k) { return new PostgresMutationSecurityConformance(a,b,c,d,e,f,g,h,i,j,k); }

    public List<String> requiredInvariants() {
        return List.of(
                SecurityInvariantIds.TENANT_ISOLATION,
                SecurityInvariantIds.RUNTIME_AUTHORIZATION,
                SecurityInvariantIds.AUTHORIZATION_OWNERSHIP,
                SecurityInvariantIds.MUTATION_DAILY_LIMIT,
                SecurityInvariantIds.ATOMIC_MUTATION,
                SecurityInvariantIds.MUTATION_ROW_LOCKING,
                SecurityInvariantIds.IDEMPOTENCY,
                SecurityInvariantIds.REPLAY_PROTECTION,
                SecurityInvariantIds.AUDIT_REQUIRED,
                SecurityInvariantIds.EXECUTION_EVIDENCE_REQUIRED);
    }

    public List<String> missingRequirements() {
        var missing = new ArrayList<String>();
        if (!usesSingleTransaction) missing.add(SecurityInvariantIds.ATOMIC_MUTATION);
        if (!locksMutationRowsDeterministically) missing.add(SecurityInvariantIds.MUTATION_ROW_LOCKING);
        if (!revalidatesAuthorizationAtExecution) missing.add(SecurityInvariantIds.RUNTIME_AUTHORIZATION);
        if (!serializesIdempotencyKeys || !persistsIdempotencyInsideTransaction) missing.add(SecurityInvariantIds.IDEMPOTENCY);
        if (!enforcesReplayProtection) missing.add(SecurityInvariantIds.REPLAY_PROTECTION);
        if (!persistsAuditInsideTransaction) missing.add(SecurityInvariantIds.AUDIT_REQUIRED);
        if (!emitsExecutionReceipt) missing.add(SecurityInvariantIds.EXECUTION_EVIDENCE_REQUIRED);
        if (!enforcesOwnership) missing.add(SecurityInvariantIds.AUTHORIZATION_OWNERSHIP);
        if (!enforcesDailyLimit) missing.add(SecurityInvariantIds.MUTATION_DAILY_LIMIT);
        if (!enforcesTenantIsolation) missing.add(SecurityInvariantIds.TENANT_ISOLATION);
        return missing.stream().distinct().sorted().toList();
    }

    public boolean isSatisfied() { return missingRequirements().isEmpty(); }

    public void ensureSatisfied() {
        var missing = missingRequirements();
        if (!missing.isEmpty())
            throw new IllegalStateException("PostgreSQL high-assurance mutation contract failed: missing " + String.join(", ", missing));
    }
}
