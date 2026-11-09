package com.foundgine.providers.gateway.http;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.mutation.SemanticMutationOperationGraph;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContextProviderExtensions;
import com.foundgine.providers.gateway.FoundgineGatewayEvent;
import com.foundgine.providers.gateway.FoundgineGatewayEventJson;
import com.foundgine.providers.gateway.FoundgineGatewayEventType;
import com.foundgine.providers.gateway.events.FoundgineGatewayCursorStatus;
import com.foundgine.providers.gateway.events.FoundgineGatewayEventCursor;
import com.foundgine.providers.gateway.events.FoundgineGatewayEventVisibility;
import com.foundgine.providers.gateway.events.FoundgineGatewayViewer;
import com.foundgine.providers.gateway.execution.FoundgineGatewayExecutionStatus;
import com.foundgine.runtime.MutationExecutionResult;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.Duration;
import java.util.LinkedHashMap;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.CancellationException;
import java.util.concurrent.CompletionException;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.ExecutionException;

/**
 * The endpoint implementations, a port of {@code FoundgineGatewayEndpointRouteBuilderExtensions}. Status codes,
 * headers, execution-lifecycle events and the SSE protocol follow the .NET handlers. {@link SecurityException} is the
 * Java counterpart of {@code UnauthorizedAccessException} (403); {@link IllegalArgumentException} of
 * {@code ArgumentException} (400).
 */
final class FoundgineGatewayHandlers {
    private static final String EXECUTION_ID_HEADER = "X-Foundgine-Execution-Id";
    private final FoundgineGatewayHttpServices s;

    FoundgineGatewayHandlers(FoundgineGatewayHttpServices services) { this.s = services; }

    // ---- discovery -------------------------------------------------------------------------------------------

    void capabilities(GatewayHttpRequest req, GatewayHttpResponse res) throws IOException {
        res.writeJson(FoundgineGatewayJsonWriter.description(s.gateway.describe()));
    }

    void capability(GatewayHttpRequest req, GatewayHttpResponse res) throws IOException {
        var id = req.pathParam("id");
        if (id == null || id.isBlank()) { res.status(400); return; }
        var capability = s.gateway.findCapability(id);
        if (capability.isEmpty()) { res.status(404); return; }
        res.writeJson(FoundgineGatewayJsonWriter.capability(capability.get()));
    }

    void semantic(GatewayHttpRequest req, GatewayHttpResponse res) throws IOException {
        res.writeJson(s.semanticJson.apply(s.semanticSource.describe()));
    }

    // ---- read ------------------------------------------------------------------------------------------------

    void read(GatewayHttpRequest req, GatewayHttpResponse res) throws Exception {
        var body = readBody(req, res);
        if (body == null) return;
        ReadIntent intent;
        try {
            intent = FoundgineGatewayReadRequestMapper.toIntent(FoundgineGatewayReadRequest.fromJson(body), s.limits);
        } catch (IllegalArgumentException invalid) { res.status(400); return; }

        var executionId = newExecutionId();
        if (!tryPublishStarted(res, executionId, "read")) return;
        ExecutionResult result;
        try {
            result = await(s.execution.executeReadAsync(intent, null, req.cancellation()), req.cancellation());
            publishBestEffort(FoundgineGatewayEventType.EXECUTION_COMPLETED, executionId, "read", "succeeded");
        } catch (SecurityException denied) {
            publishBestEffort(FoundgineGatewayEventType.EXECUTION_FAILED, executionId, "read", "authorization_denied");
            res.status(403);
            return;
        } catch (Exception failure) {
            publishFailure(req, executionId, "read", failure);
            throw failure;
        }
        res.header(EXECUTION_ID_HEADER, executionId);

        String json;
        try {
            json = FoundgineGatewayReadResponseMapper.toJson(FoundgineGatewayReadResponseMapper.map(result));
        } catch (UnsupportedOperationException unmapped) {
            // A result type without an explicit wire mapping is never serialized via reflection.
            res.status(501);
            return;
        }
        res.writeJson(json);
    }

    // ---- mutations -------------------------------------------------------------------------------------------

    void dryRun(GatewayHttpRequest req, GatewayHttpResponse res) throws IOException {
        var graph = readMutationGraph(req, res);
        if (graph == null) return;
        String json;
        try {
            var result = s.execution.dryRunMutation(graph);
            var ops = result.operations().stream().map(x -> new FoundgineGatewayMutationResponses.PlanOperation(
                    x.index(), x.entity(), x.kind(), x.fields(), x.returnFields())).toList();
            json = FoundgineGatewayJsonWriter.dryRun(
                    new FoundgineGatewayMutationResponses.DryRun(result.planFingerprint(), ops, result.effects()));
        } catch (SecurityException denied) { res.status(403); return; }
        catch (IllegalArgumentException invalid) { res.status(400); return; }
        res.writeJson(json);
    }

    void createApproval(GatewayHttpRequest req, GatewayHttpResponse res) throws IOException {
        var graph = readMutationGraph(req, res);
        if (graph == null) return;
        String json;
        try {
            var receipt = s.approvals.create(graph);
            json = FoundgineGatewayJsonWriter.approval(new FoundgineGatewayMutationResponses.Approval(
                    receipt.handle(), receipt.expiresAt(), receipt.planFingerprint()));
        } catch (SecurityException denied) { res.status(403); return; }
        catch (IllegalArgumentException invalid) { res.status(400); return; }
        res.status(201);
        res.writeJson(json);
    }

    void executeApproved(GatewayHttpRequest req, GatewayHttpResponse res) throws Exception {
        var handle = req.pathParam("handle");
        if (handle == null) { res.status(400); return; }
        var executionId = newExecutionId();
        MutationExecutionResult result;
        try {
            publishStarted(executionId, "mutation");
            Optional<MutationExecutionResult> outcome = await(s.approvals.executeAsync(handle), req.cancellation());
            if (outcome.isEmpty()) {
                publishBestEffort(FoundgineGatewayEventType.EXECUTION_FAILED, executionId, "mutation", "approval_unavailable");
                res.status(404);
                return;
            }
            result = outcome.get();
            publishBestEffort(FoundgineGatewayEventType.EXECUTION_COMPLETED, executionId, "mutation", "succeeded");
        } catch (SecurityException denied) {
            publishBestEffort(FoundgineGatewayEventType.EXECUTION_FAILED, executionId, "mutation", "authorization_denied");
            res.status(403);
            return;
        } catch (IllegalArgumentException invalid) {
            publishBestEffort(FoundgineGatewayEventType.EXECUTION_FAILED, executionId, "mutation", "invalid_request");
            res.status(400);
            return;
        } catch (Exception failure) {
            publishFailure(req, executionId, "mutation", failure);
            throw failure;
        }
        res.header(EXECUTION_ID_HEADER, executionId);
        res.writeJson(FoundgineGatewayJsonWriter.execution(new FoundgineGatewayMutationResponses.Execution(
                result.planFingerprint(), result.resultFingerprint(), result.approvalId(), result.approvedBy(),
                result.result().results().size(), result.result().totalAffectedRows())));
    }

    void revokeApproval(GatewayHttpRequest req, GatewayHttpResponse res) {
        var handle = req.pathParam("handle");
        if (handle == null) { res.status(400); return; }
        try { res.status(s.approvals.revoke(handle) ? 204 : 404); }
        catch (SecurityException denied) { res.status(403); }
        catch (IllegalArgumentException invalid) { res.status(400); }
    }

    // ---- execution status ------------------------------------------------------------------------------------

    void executionStatus(GatewayHttpRequest req, GatewayHttpResponse res) throws IOException {
        var id = req.pathParam("id");
        if (id == null || !isExecutionId(id)) { res.status(400); return; }
        String ownerDigest;
        try {
            var security = SecurityExecutionContextProviderExtensions.requireSecurityExecutionContext(
                    s.security, "Gateway", "read execution status");
            ownerDigest = s.digester.owner(security.tenant(), security.subject());
        } catch (SecurityException denied) { res.status(403); return; }

        var events = s.eventStore.readByCorrelationId(id, 100);
        FoundgineGatewayEvent started = null;
        for (var e : events) if (e.type() == FoundgineGatewayEventType.EXECUTION_STARTED) { started = e; break; }
        var owner = started == null ? null : started.attributes().get("owner.digest");
        if (owner == null || !MessageDigest.isEqual(owner.getBytes(StandardCharsets.UTF_8), ownerDigest.getBytes(StandardCharsets.UTF_8))) {
            // Unknown and not-yours are indistinguishable on purpose.
            res.status(404);
            return;
        }
        var latest = events.get(events.size() - 1);
        var status = switch (latest.type()) {
            case EXECUTION_COMPLETED -> "succeeded";
            case EXECUTION_FAILED -> "failed";
            default -> "running";
        };
        var kind = started.attributes().getOrDefault("operation.kind", "unknown");
        var outcome = latest.attributes().get("outcome.code");
        res.writeJson(FoundgineGatewayJsonWriter.executionStatus(new FoundgineGatewayExecutionStatus(id, status, kind,
                started.occurredAt(), status.equals("running") ? null : latest.occurredAt(), outcome)));
    }

    // ---- event stream (SSE) ----------------------------------------------------------------------------------

    void streamEvents(GatewayHttpRequest req, GatewayHttpResponse res) throws Exception {
        var options = s.streamOptions;
        // Fail closed: the stream is audience-scoped, so the caller must have a trusted security context.
        FoundgineGatewayViewer viewer;
        try {
            var security = SecurityExecutionContextProviderExtensions.requireSecurityExecutionContext(
                    s.security, "Gateway", "stream events");
            boolean isOperator = options.operatorPolicy() != null && s.authorizer != null
                    && s.authorizer.satisfiesPolicy(req, options.operatorPolicy());
            viewer = new FoundgineGatewayViewer(s.digester.owner(security.tenant(), security.subject()),
                    s.digester.tenant(security.tenant()), isOperator);
        } catch (SecurityException denied) { res.status(403); return; }

        long latest = s.eventStore.getLatestSequence();
        var raw = req.hasHeader("Last-Event-ID") ? req.header("Last-Event-ID")
                : req.queryParam("after") == null ? "" : req.queryParam("after");
        var cursor = FoundgineGatewayEventCursor.resolve(raw, latest, options.maxReplayWindow());
        res.header("X-Foundgine-Latest-Sequence", Long.toString(latest));
        if (cursor.status() == FoundgineGatewayCursorStatus.INVALID) { res.status(400); return; }
        if (cursor.status() == FoundgineGatewayCursorStatus.TOO_OLD) { res.status(410); return; }

        res.status(200);
        res.header("Content-Type", "text/event-stream; charset=utf-8");
        res.header("Cache-Control", "no-cache, no-store");
        res.header("X-Accel-Buffering", "no");

        var token = req.cancellation();
        long heartbeat = options.heartbeatInterval().toNanos();
        try (var subscription = s.hub.subscribeAfter(cursor.after());
             var onAbort = token.register(subscription::close)) {
            var sse = new GatewaySseWriter(res.body());
            sse.retry(options.reconnectDelay().toMillis());
            long lastScanned = cursor.after(), lastCheckpoint = cursor.after();
            long lastWrite = System.nanoTime();
            while (!token.isCancellationRequested()) {
                long remaining = heartbeat - (System.nanoTime() - lastWrite);
                Optional<FoundgineGatewayEvent> next;
                try { next = remaining > 0 ? subscription.poll(Duration.ofNanos(remaining)) : Optional.empty(); }
                catch (InterruptedException stopping) { Thread.currentThread().interrupt(); return; }
                if (token.isCancellationRequested()) return;
                if (next.isEmpty()) {
                    // Keep-alive. The id line moves the client's resume cursor past events it may not see.
                    sse.keepAlive(lastScanned > lastCheckpoint ? lastScanned : null);
                    lastCheckpoint = Math.max(lastCheckpoint, lastScanned);
                    lastWrite = System.nanoTime();
                    continue;
                }
                var gatewayEvent = next.get();
                lastScanned = gatewayEvent.sequence();
                if (!FoundgineGatewayEventVisibility.isVisible(gatewayEvent, viewer)) continue;
                sse.event(gatewayEvent.sequence(), FoundgineGatewayEventJson.typeName(gatewayEvent.type()),
                        FoundgineGatewayEventJson.toJson(FoundgineGatewayEventVisibility.toPublic(gatewayEvent)));
                lastCheckpoint = gatewayEvent.sequence();
                lastWrite = System.nanoTime();
            }
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private SemanticMutationOperationGraph readMutationGraph(GatewayHttpRequest req, GatewayHttpResponse res) throws IOException {
        var body = readBody(req, res);
        if (body == null) return null;
        try {
            return FoundgineGatewayMutationRequestMapper.toGraph(FoundgineGatewayMutationRequest.fromJson(body), s.limits);
        } catch (IllegalArgumentException invalid) { res.status(400); return null; }
    }

    /** Reads at most {@code maxRequestBytes}; sets 413 and returns null when the body is larger. */
    private String readBody(GatewayHttpRequest req, GatewayHttpResponse res) throws IOException {
        long max = s.limits.maxRequestBytes;
        if (req.contentLength() > max) { res.status(413); return null; }
        var buffer = new ByteArrayOutputStream();
        var chunk = new byte[8192];
        long total = 0;
        int n;
        var in = req.body();
        while ((n = in.read(chunk)) != -1) {
            total += n;
            if (total > max) { res.status(413); return null; }
            buffer.write(chunk, 0, n);
        }
        try {
            var text = StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT)
                    .onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(buffer.toByteArray())).toString();
            if (text.isBlank()) { res.status(400); return null; }
            return text;
        } catch (CharacterCodingException notUtf8) { res.status(400); return null; }
    }

    private static String newExecutionId() { return UUID.randomUUID().toString().replace("-", ""); }

    private static boolean isExecutionId(String id) {
        if (id.length() != 32) return false;
        for (int i = 0; i < 32; i++) {
            char c = id.charAt(i);
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
        }
        return true;
    }

    /** Publishes ExecutionStarted; a missing security context is a 403 and nothing runs. */
    private boolean tryPublishStarted(GatewayHttpResponse res, String executionId, String kind) {
        try { publishStarted(executionId, kind); return true; }
        catch (SecurityException denied) { res.status(403); return false; }
    }

    private void publishStarted(String executionId, String kind) {
        s.publisher.publish(FoundgineGatewayEventType.EXECUTION_STARTED, "execution", executionId, null, attributes(kind, null));
    }

    /** Execution may have committed, so a notification failure must not misreport its outcome. */
    private void publishBestEffort(FoundgineGatewayEventType type, String executionId, String kind, String outcome) {
        try { s.publisher.publish(type, "execution", executionId, null, attributes(kind, outcome)); }
        catch (RuntimeException ignored) { /* best effort */ }
    }

    private void publishFailure(GatewayHttpRequest req, String executionId, String kind, Exception failure) {
        boolean cancelled = failure instanceof CancellationException && req.cancellation().isCancellationRequested();
        publishBestEffort(FoundgineGatewayEventType.EXECUTION_FAILED, executionId, kind, cancelled ? "cancelled" : "failed");
    }

    private LinkedHashMap<String, String> attributes(String kind, String outcome) {
        var security = SecurityExecutionContextProviderExtensions.requireSecurityExecutionContext(
                s.security, "Gateway", "publish execution lifecycle");
        var values = new LinkedHashMap<String, String>();
        values.put("operation.kind", kind);
        values.put("owner.digest", s.digester.owner(security.tenant(), security.subject()));
        if (outcome != null) values.put("outcome.code", outcome);
        return values;
    }

    /** Waits for a stage, cancelling it if the request is aborted, and surfaces the real failure. */
    private static <T> T await(CompletionStage<T> stage, CancellationToken token) throws Exception {
        var future = stage.toCompletableFuture();
        try (var registration = token.register(() -> future.cancel(true))) {
            return future.get();
        } catch (ExecutionException failed) {
            throw unwrap(failed);
        } catch (InterruptedException interrupted) {
            Thread.currentThread().interrupt();
            throw new CancellationException("Interrupted while waiting for the gateway operation.");
        }
    }

    private static Exception unwrap(Throwable failure) {
        Throwable cause = failure;
        while ((cause instanceof ExecutionException || cause instanceof CompletionException) && cause.getCause() != null)
            cause = cause.getCause();
        if (cause instanceof Error error) throw error;
        return cause instanceof Exception e ? e : new IllegalStateException(cause);
    }
}
