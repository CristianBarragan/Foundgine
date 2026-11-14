package com.foundgine.providers.tools.mcp;

import java.util.List;
import java.util.Objects;
import java.util.concurrent.Executor;

/**
 * The HTTP decisions of the MCP SSE endpoint, free of Spring and servlets: which status a {@code GET} or {@code DELETE} gets,
 * and the live stream when a {@code GET} is accepted. The same behaviour as the C# {@code FoundgineMcpSseTransport}; the Spring
 * controller is a thin adapter over it, and both runtimes run {@code mcp-sse-transport-scenarios.json} against it.
 *
 * <p>Order of checks for {@code GET}: origin (403), the session gate (400 missing id, 404 not acceptable), then opening the
 * stream (400 invalid {@code Last-Event-ID}, 409 a stream is already open, 410 id no longer retained, 429 a limit is reached,
 * 404 the session ended meanwhile). {@code DELETE}: origin (403), gate (400/404), then 204 and the buffer is retired.
 */
public final class FoundgineMcpSseTransport {
    private FoundgineMcpSseTransport() {}

    /**
     * @param allowedOrigins origins accepted in {@code Origin}; a request without one is accepted
     * @param maxStreamSeconds a stream older than this is ended at its next heartbeat; 0 = no limit
     * @param limits open-stream caps; null = unlimited
     * @param audit receives SSE_OPENED / SSE_REJECTED / SSE_CLOSED; null = none
     */
    public record Settings(List<String> allowedOrigins, int maxStreamSeconds, FoundgineMcpSseLimits limits, FoundgineMcpAuditSink audit) {}

    /** @param stream non-null only when {@code status} is 200; the caller must {@link FoundgineMcpSseStream#start()} it */
    public record GetResult(int status, FoundgineMcpSseStream stream) {}

    public static boolean originRefused(List<String> allowedOrigins, String origin) {
        return origin != null && !origin.isEmpty() && (allowedOrigins == null || !allowedOrigins.contains(origin));
    }

    public static GetResult get(FoundgineMcpSessionGate gate, FoundgineMcpSseRegistry registry, Settings settings, String origin,
            String sessionId, String owner, String lastEventId, FoundgineMcpSseStream.Sink sink, Executor executor) {
        Objects.requireNonNull(gate, "gate");
        Objects.requireNonNull(registry, "registry");
        Objects.requireNonNull(settings, "settings");
        if (originRefused(settings.allowedOrigins(), origin)) return new GetResult(403, null);
        var outcome = gate.admitGet(sessionId, owner);
        if (!outcome.proceeds()) return new GetResult(outcome.httpStatus(), null);
        var options = new FoundgineMcpSseStream.Options(sessionId, owner, settings.limits(), settings.maxStreamSeconds() * 1000L,
                settings.audit(), null);
        var opened = FoundgineMcpSseStream.open(registry.stream(sessionId), lastEventId, sink, executor, options);
        return switch (opened.status()) {
            case OK -> new GetResult(200, opened.stream());
            case INVALID -> new GetResult(400, null);
            case BUSY -> new GetResult(409, null);
            case EVICTED -> new GetResult(410, null);
            case LIMIT -> new GetResult(429, null);
            case GONE -> new GetResult(404, null);
        };
    }

    /** Terminates the caller's session and, on success, retires its buffer (which closes the attached stream). Returns the status. */
    public static int delete(FoundgineMcpSessionGate gate, FoundgineMcpSseRegistry registry, Settings settings, String origin,
            String sessionId, String owner) {
        Objects.requireNonNull(gate, "gate");
        Objects.requireNonNull(settings, "settings");
        if (originRefused(settings.allowedOrigins(), origin)) return 403;
        var outcome = gate.admitDelete(sessionId, owner);
        if (outcome.proceeds() && registry != null) registry.remove(sessionId);
        return outcome.httpStatus();
    }
}
