package com.foundgine.providers.gateway.approvals;

import com.foundgine.runtime.MutationPlanApproval;
import java.time.Clock;
import java.time.Instant;
import java.util.Objects;
import java.util.Optional;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Single-process development store. Intentionally not durable or shared across replicas; production hosts must
 * register a distributed implementation of {@link IFoundgineGatewayApprovalStore}.
 */
public final class InMemoryFoundgineGatewayApprovalStore implements IAtomicFoundgineGatewayApprovalStore {
    private record Entry(MutationPlanApproval approval, String subject, String tenant, Instant expiresAt) { }
    private final ConcurrentHashMap<String, Entry> entries = new ConcurrentHashMap<>();
    private final Clock clock;
    private final java.util.List<FoundgineGatewayApprovalEvidence> evidence = new java.util.ArrayList<>();

    public InMemoryFoundgineGatewayApprovalStore() { this(Clock.systemUTC()); }
    public InMemoryFoundgineGatewayApprovalStore(Clock clock) { this.clock = Objects.requireNonNull(clock, "clock"); }

    @Override public synchronized void store(String handleDigest, MutationPlanApproval approval, String subject, String tenant, Instant expiresAt) {
        removeExpired(clock.instant());
        if (handleDigest == null || handleDigest.isBlank()) throw new IllegalArgumentException("handleDigest is required");
        Objects.requireNonNull(approval, "approval");
        if (subject == null || subject.isBlank()) throw new IllegalArgumentException("subject is required");
        if (entries.putIfAbsent(handleDigest, new Entry(approval, subject, tenant, expiresAt)) != null)
            throw new IllegalStateException("Approval handle collision.");
    }


    @Override public synchronized void storeWithEvidence(String handleDigest, MutationPlanApproval approval,
            FoundgineGatewayApprovalEvidence event) {
        Objects.requireNonNull(event, "event");
        // Development-only implementation models a single atomic commit; it is not durable.
        store(handleDigest, approval, event.subject(), event.tenant(), event.expiresAt());
        evidence.add(event);
    }

    /** Development-only evidence view; this is not durable audit storage. */
    public synchronized java.util.List<FoundgineGatewayApprovalEvidence> evidence() {
        return java.util.List.copyOf(evidence);
    }

    @Override public synchronized Optional<MutationPlanApproval> take(String handleDigest, String subject, String tenant, Instant now) {
        var entry = entries.get(handleDigest);
        if (entry == null || !Objects.equals(entry.subject, subject) || !Objects.equals(entry.tenant, tenant)) return Optional.empty();
        boolean removed = entries.remove(handleDigest, entry);   // atomic: only one taker wins
        if (!removed || !entry.expiresAt.isAfter(now)) return Optional.empty();
        return Optional.of(entry.approval);
    }

    @Override public synchronized boolean revoke(String handleDigest, String subject, String tenant) {
        var entry = entries.get(handleDigest);
        if (entry == null || !Objects.equals(entry.subject, subject) || !Objects.equals(entry.tenant, tenant)) return false;
        return entries.remove(handleDigest, entry);
    }

    private void removeExpired(Instant now) { entries.entrySet().removeIf(e -> !e.getValue().expiresAt.isAfter(now)); }
}
