package com.foundgine.core.semantic.security;

import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.NoSuchElementException;

/**
 * Port of {@code Foundgine.Core.Semantic.Security.SecurityInvariantRegistry}.
 *
 * <p>Canonical registry. The registry is deliberately provider-neutral so
 * every adapter, planner and provider can reason about the same invariant
 * vocabulary.
 */
public final class SecurityInvariantRegistry {

    private static final Map<String, SecurityInvariant> ALL = buildAll();

    private SecurityInvariantRegistry() {
    }

    private static Map<String, SecurityInvariant> buildAll() {
        Map<String, SecurityInvariant> all = new LinkedHashMap<>();

        put(all, SecurityInvariantIds.AUTHORIZATION_REQUIRED, "Authorization required",
                "The capability may execute only when its effective authorization policy permits the operation.",
                SecurityInvariantPhase.SEMANTIC_RESOLUTION, true);
        put(all, SecurityInvariantIds.RUNTIME_AUTHORIZATION, "Runtime authorization",
                "Authorization must be evaluated against current execution context rather than trusted model-supplied authority.",
                SecurityInvariantPhase.EXECUTION, true);
        put(all, SecurityInvariantIds.AUTHORIZATION_OWNERSHIP, "Authorization ownership",
                "A consequential account mutation must not operate on accounts the acting principal does not own, even if an upstream authorization dependency incorrectly permits the request.",
                SecurityInvariantPhase.EXECUTION, true);
        put(all, SecurityInvariantIds.TENANT_ISOLATION, "Tenant isolation",
                "Data and mutations must remain within the effective tenant boundary.",
                SecurityInvariantPhase.EXECUTION, true);
        put(all, SecurityInvariantIds.FIELD_VISIBILITY, "Field visibility",
                "Fields not exposed by the effective semantic policy must not become selectable or writable.",
                SecurityInvariantPhase.SEMANTIC_RESOLUTION, true);
        put(all, SecurityInvariantIds.RELATIONSHIP_VISIBILITY, "Relationship visibility",
                "Relationships not exposed by the effective semantic policy must not become traversable.",
                SecurityInvariantPhase.SEMANTIC_RESOLUTION, true);
        put(all, SecurityInvariantIds.PARAMETERIZED_VALUES, "Parameterized values",
                "Untrusted values must remain data parameters and must not become executable provider syntax.",
                SecurityInvariantPhase.PROVIDER_COMPILATION, true);
        put(all, SecurityInvariantIds.PLAN_CACHE_CONTEXT_ISOLATION, "Plan cache context isolation",
                "Reusable provider plans must not freeze request-specific authority or tenant values.",
                SecurityInvariantPhase.PLANNING, true);
        put(all, SecurityInvariantIds.ATOMIC_MUTATION, "Atomic mutation",
                "A capability requiring atomic mutation must preserve its state transition as one transactionally consistent operation.",
                SecurityInvariantPhase.EXECUTION, true);
        put(all, SecurityInvariantIds.MUTATION_ROW_LOCKING, "Mutation row locking",
                "Protected mutations must lock all affected state deterministically before applying the state transition.",
                SecurityInvariantPhase.EXECUTION, true);
        put(all, SecurityInvariantIds.IDEMPOTENCY, "Idempotency",
                "Repeated requests carrying the same semantic idempotency identity must not repeat the protected side effect.",
                SecurityInvariantPhase.EXECUTION, true);
        put(all, SecurityInvariantIds.REPLAY_PROTECTION, "Replay protection",
                "An idempotency identity cannot be rebound to materially different actor, tenant, target or value context.",
                SecurityInvariantPhase.EXECUTION, true);
        put(all, SecurityInvariantIds.AUDIT_REQUIRED, "Audit required",
                "Protected mutations must emit the required audit evidence as part of successful execution.",
                SecurityInvariantPhase.EVIDENCE, true);
        put(all, SecurityInvariantIds.EXECUTION_EVIDENCE_REQUIRED, "Execution evidence required",
                "Protected execution must produce an evidence-bearing execution receipt.",
                SecurityInvariantPhase.EVIDENCE, true);
        put(all, SecurityInvariantIds.MUTATION_DAILY_LIMIT, "Mutation daily limit",
                "A consequential transfer must not increase an account's daily transferred amount beyond its configured daily limit, including when multiple transfers share one batch.",
                SecurityInvariantPhase.EXECUTION, true);
        put(all, SecurityInvariantIds.TRANSACTION_READ_COMMITTED_ISOLATION, "Transaction read-committed isolation",
                "A protected mutation's transaction must run under an explicit read-committed (or stronger) isolation level rather than an unspecified default.",
                SecurityInvariantPhase.EXECUTION, true);

        return Collections.unmodifiableMap(all);
    }

    private static void put(Map<String, SecurityInvariant> map, String id, String name, String description,
            SecurityInvariantPhase phase, boolean mustBePreservedByProvider) {
        map.put(id, new SecurityInvariant(id, name, description, phase, mustBePreservedByProvider));
    }

    public static Collection<SecurityInvariant> allInvariants() {
        return Collections.unmodifiableCollection(new ArrayList<>(ALL.values()));
    }

    public static SecurityInvariant get(String id) {
        SecurityInvariant invariant = ALL.get(id);
        if (invariant == null) {
            throw new NoSuchElementException("Unknown security invariant '" + id + "'.");
        }
        return invariant;
    }

    public static boolean contains(String id) {
        return ALL.containsKey(id);
    }

    public static SecurityInvariantSet createSet(Iterable<String> ids) {
        LinkedHashSet<String> distinctIds = new LinkedHashSet<>();
        for (String id : ids) {
            distinctIds.add(id);
        }

        List<SecurityInvariant> invariants = new ArrayList<>();
        for (String id : distinctIds) {
            invariants.add(get(id));
        }
        invariants.sort((a, b) -> a.id().compareTo(b.id()));

        return new SecurityInvariantSet(Collections.unmodifiableList(invariants));
    }
}
