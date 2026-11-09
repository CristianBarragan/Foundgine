package com.foundgine.providers.gateway.approvals;

import com.foundgine.core.semantic.mutation.SemanticMutationOperationGraph;
import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContextProviderExtensions;
import com.foundgine.providers.gateway.execution.IFoundgineGatewayExecution;
import com.foundgine.runtime.MutationExecutionResult;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.security.SecureRandom;
import java.time.Clock;
import java.time.Duration;
import java.util.Base64;
import java.util.HexFormat;
import java.util.Objects;
import java.util.Optional;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;

/** Creates, revokes and consumes opaque, one-time approval handles. Only a digest of the handle is stored. */
public final class FoundgineGatewayApprovalService {
    private static final SecureRandom RANDOM = new SecureRandom();
    private static final HexFormat HEX = HexFormat.of().withUpperCase();
    private final IFoundgineGatewayExecution execution;
    private final IFoundgineGatewayApprovalStore store;
    private final ISecurityExecutionContextProvider security;
    private final Clock clock;
    private final Duration lifetime;

    public FoundgineGatewayApprovalService(IFoundgineGatewayExecution execution, IFoundgineGatewayApprovalStore store,
            ISecurityExecutionContextProvider security) {
        this(execution, store, security, Clock.systemUTC(), null);
    }

    public FoundgineGatewayApprovalService(IFoundgineGatewayExecution execution, IFoundgineGatewayApprovalStore store,
            ISecurityExecutionContextProvider security, Clock clock, Duration lifetime) {
        this.execution = Objects.requireNonNull(execution, "execution");
        this.store = Objects.requireNonNull(store, "store");
        this.security = Objects.requireNonNull(security, "security");
        this.clock = clock == null ? Clock.systemUTC() : clock;
        this.lifetime = lifetime == null ? Duration.ofMinutes(10) : lifetime;
        if (this.lifetime.isZero() || this.lifetime.isNegative() || this.lifetime.compareTo(Duration.ofHours(24)) > 0)
            throw new IllegalArgumentException("Approval lifetime must be greater than zero and no more than 24 hours.");
    }

    public FoundgineGatewayApprovalReceipt create(SemanticMutationOperationGraph graph) {
        Objects.requireNonNull(graph, "graph");
        var identity = SecurityExecutionContextProviderExtensions.requireSecurityExecutionContext(
                security, "Gateway", "gateway mutation approval");
        var approval = execution.approveMutation(graph);
        var approved = approval.request().security();
        if (approved == null || !Objects.equals(approved.subject(), identity.subject())
                || !Objects.equals(approved.tenant(), identity.tenant()))
            throw new SecurityException("The approval security context changed while the approval was being created.");
        var raw = new byte[32];
        RANDOM.nextBytes(raw);
        var handle = Base64.getUrlEncoder().withoutPadding().encodeToString(raw);
        var expires = clock.instant().plus(lifetime);
        store.store(digest(handle), approval, identity.subject(), identity.tenant(), expires);
        return new FoundgineGatewayApprovalReceipt(handle, expires, approval.planFingerprint());
    }

    /** Consumes the handle (one-time) and executes. Empty when the handle is unknown, expired, revoked or not the caller's. */
    public CompletionStage<Optional<MutationExecutionResult>> executeAsync(String handle) {
        try {
            validateHandle(handle);
            var identity = SecurityExecutionContextProviderExtensions.requireSecurityExecutionContext(
                    security, "Gateway", "gateway approved mutation execution");
            var approval = store.take(digest(handle), identity.subject(), identity.tenant(), clock.instant());
            if (approval.isEmpty()) return CompletableFuture.completedFuture(Optional.empty());
            return execution.executeApprovedMutationAsync(approval.get()).thenApply(Optional::of);
        } catch (RuntimeException failure) {
            return CompletableFuture.failedFuture(failure);
        }
    }

    public boolean revoke(String handle) {
        validateHandle(handle);
        var identity = SecurityExecutionContextProviderExtensions.requireSecurityExecutionContext(
                security, "Gateway", "gateway mutation approval revocation");
        return store.revoke(digest(handle), identity.subject(), identity.tenant());
    }

    private static void validateHandle(String handle) {
        if (handle == null || handle.length() != 43 || !handle.chars().allMatch(c ->
                (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
            throw new IllegalArgumentException("Approval handle is invalid.");
    }

    private static String digest(String handle) {
        try { return HEX.formatHex(MessageDigest.getInstance("SHA-256").digest(handle.getBytes(StandardCharsets.UTF_8))); }
        catch (NoSuchAlgorithmException e) { throw new IllegalStateException(e); }
    }
}
