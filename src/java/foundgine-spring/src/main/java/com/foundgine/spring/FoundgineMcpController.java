package com.foundgine.spring;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.providers.tools.mcp.FoundgineMcpAuditSink;
import com.foundgine.providers.tools.mcp.FoundgineMcpServer;
import com.foundgine.providers.tools.mcp.FoundgineMcpSessionGate;
import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager;
import com.foundgine.providers.tools.mcp.FoundgineMcpSseBuffer;
import com.foundgine.providers.tools.mcp.FoundgineMcpSseLimits;
import com.foundgine.providers.tools.mcp.FoundgineMcpSseRegistry;
import com.foundgine.providers.tools.mcp.FoundgineMcpSseStream;
import com.foundgine.providers.tools.mcp.FoundgineMcpSseTransport;
import java.security.Principal;
import java.io.IOException;
import java.util.List;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.Executor;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.servlet.mvc.method.annotation.SseEmitter;

/**
 * Streamable-HTTP style MCP endpoint (JSON responses; sessions only when a {@link FoundgineMcpSessionGate} is supplied; a replayable SSE GET only when a {@link FoundgineMcpSseRegistry} is supplied as well). Browsers' cross-origin
 * requests are refused unless their {@code Origin} is listed in {@code foundgine.mcp.allowed-origins}, which is the
 * MCP-recommended DNS-rebinding defence; requests without an Origin (non-browser agents) are not affected. Authentication
 * is the application's job and must run before this controller. The tools are invoked on the request thread, so
 * thread-bound identity (Spring Security's context holder) is visible to the host's security provider.
 *
 * <p><b>Sessions</b> (optional, {@code foundgine.mcp.sessions-enabled=true}): {@code initialize} issues
 * {@code Mcp-Session-Id}, other requests need it (400 missing, 404 unknown), {@code DELETE} terminates (204). A session
 * never replaces authentication: it is bound to the authenticated {@link Principal} name, which must come from your
 * security layer; without an authenticated principal sessions are unbound.
 *
 * <p><b>SSE</b> (optional, needs sessions): {@code GET} with {@code Accept: text/event-stream} and a valid
 * {@code Mcp-Session-Id} opens the session's one live stream: the retained events after {@code Last-Event-ID} are replayed
 * (spec {@code mcp-sse-replay-v1}) and every message later published with {@link
 * com.foundgine.providers.tools.mcp.FoundgineMcpSsePublisher} follows, in order and exactly once. A comment line is sent every
 * {@code heartbeatSeconds} to keep proxies from closing an idle connection, and a stream older than {@code maxStreamSeconds} is
 * ended so the client reconnects with {@code Last-Event-ID}. Statuses: 400 invalid {@code Last-Event-ID}, 404 the session ended
 * meanwhile, 409 a stream is already open for the session, 410 the id fell out of the retained window (resynchronise), 429 the
 * total or per-principal stream limit is reached, 405 SSE not enabled. {@code DELETE} closes the open stream. The buffer is
 * created when the session is issued; a periodic sweep (every {@code sweepSeconds}) purges expired sessions and drops the buffers
 * of sessions that are no longer live, closing their streams. Opens, refusals and closes go to the audit sink as
 * {@code SSE_OPENED}/{@code SSE_REJECTED}/{@code SSE_CLOSED}. UNVERIFIED: written without the Spring jars.
 */
@RestController
@RequestMapping("${foundgine.mcp.path:/mcp}")
public class FoundgineMcpController {
    private static final ObjectMapper JSON = new ObjectMapper();
    private final FoundgineMcpServer server;
    private final List<String> allowedOrigins;
    private final FoundgineMcpSessionGate sessions;
    private final FoundgineMcpSseRegistry sse;
    private final SseOptions sseOptions;
    private final FoundgineMcpSseTransport.Settings transport;
    private final Executor writer = Executors.newVirtualThreadPerTaskExecutor();
    private final ScheduledExecutorService heartbeats = Executors.newSingleThreadScheduledExecutor(r -> {
        var t = new Thread(r, "foundgine-mcp-sse-heartbeat");
        t.setDaemon(true);
        return t;
    });
    private final ConcurrentHashMap<String, FoundgineMcpSseStream> open = new ConcurrentHashMap<>();

    public FoundgineMcpController(FoundgineMcpServer server, List<String> allowedOrigins) {
        this(server, allowedOrigins, null);
    }

    /** @param sessions the session gate, or {@code null} for the stateless endpoint */
    public FoundgineMcpController(FoundgineMcpServer server, List<String> allowedOrigins, FoundgineMcpSessionGate sessions) {
        this(server, allowedOrigins, sessions, null);
    }

    /** Tuning of the SSE endpoint. */
    public record SseOptions(int heartbeatSeconds, int maxStreamSeconds, int sweepSeconds, FoundgineMcpSseLimits limits,
                             FoundgineMcpAuditSink audit) {
        public SseOptions {
            if (heartbeatSeconds <= 0) throw new IllegalArgumentException("Heartbeat seconds must be positive.");
            if (maxStreamSeconds < 0) throw new IllegalArgumentException("Maximum stream seconds cannot be negative (0 = no limit).");
            if (sweepSeconds <= 0) throw new IllegalArgumentException("Sweep seconds must be positive.");
            limits = limits == null ? new FoundgineMcpSseLimits(5_000, 10) : limits;
            audit = audit == null ? FoundgineMcpAuditSink.NONE : audit;
        }

        /** Heartbeat 25 s, streams end after an hour, sweep every minute, 5000 streams in total and 10 per principal. */
        public static SseOptions defaults() { return new SseOptions(25, 3600, 60, null, null); }
    }

    /** @param sse the replay buffers, or {@code null} for no SSE endpoint (GET answers 405); ignored without sessions */
    public FoundgineMcpController(FoundgineMcpServer server, List<String> allowedOrigins, FoundgineMcpSessionGate sessions,
            FoundgineMcpSseRegistry sse) {
        this(server, allowedOrigins, sessions, sse, SseOptions.defaults());
    }

    public FoundgineMcpController(FoundgineMcpServer server, List<String> allowedOrigins, FoundgineMcpSessionGate sessions,
            FoundgineMcpSseRegistry sse, SseOptions sseOptions) {
        this.sseOptions = java.util.Objects.requireNonNull(sseOptions, "sseOptions");
        this.sse = sessions == null ? null : sse;
        this.server = server;
        this.allowedOrigins = allowedOrigins == null ? List.of() : allowedOrigins.stream().map(String::trim).filter(x -> !x.isEmpty()).toList();
        this.sessions = sessions;
        this.transport = new FoundgineMcpSseTransport.Settings(this.allowedOrigins, sseOptions.maxStreamSeconds(), sseOptions.limits(), sseOptions.audit());
        if (this.sse != null) {
            heartbeats.scheduleWithFixedDelay(this::sweep, sseOptions.sweepSeconds(), sseOptions.sweepSeconds(), TimeUnit.SECONDS);
        }
    }

    /**
     * Purges expired sessions and drops the SSE buffers of sessions that are no longer live (which closes their streams). Runs
     * on a timer; hosts may also call it. Never throws.
     */
    public void sweep() {
        if (sessions == null || sse == null) return;
        try {
            sessions.purgeExpired();
            sse.sweep(sessions::isLive);
        } catch (RuntimeException ignored) { /* the next run retries; a failing store must not stop the timer */ }
    }

    @PostMapping(consumes = MediaType.APPLICATION_JSON_VALUE)
    public ResponseEntity<String> post(
            @RequestBody String body,
            @RequestHeader(value = "Origin", required = false) String origin,
            @RequestHeader(value = FoundgineMcpSessionManager.HEADER_NAME, required = false) String sessionId,
            Principal principal) {
        if (originRefused(origin)) return ResponseEntity.status(HttpStatus.FORBIDDEN).build();
        String issued = null;
        if (sessions != null) {
            var outcome = sessions.admitPost(methodOf(body), sessionId, principal == null ? null : principal.getName());
            if (!outcome.proceeds()) return ResponseEntity.status(outcome.httpStatus()).build();
            issued = outcome.issuedSessionId();
        }
        var reply = server.handleAsync(body).toCompletableFuture().join();
        var response = reply.isEmpty() ? ResponseEntity.accepted() : ResponseEntity.ok().contentType(MediaType.APPLICATION_JSON);
        if (issued != null) {
            if (sse != null) sse.stream(issued);   // so messages published before the first GET are kept for it
            response = response.header(FoundgineMcpSessionManager.HEADER_NAME, issued);
        }
        return reply.isEmpty() ? response.build() : response.body(reply.get());
    }

    /** Opens the session's live SSE stream (replay, then live). 405 unless sessions and SSE are both enabled. */
    @GetMapping(produces = MediaType.TEXT_EVENT_STREAM_VALUE)
    public ResponseEntity<SseEmitter> get(
            @RequestHeader(value = "Origin", required = false) String origin,
            @RequestHeader(value = FoundgineMcpSessionManager.HEADER_NAME, required = false) String sessionId,
            @RequestHeader(value = "Last-Event-ID", required = false) String lastEventId,
            Principal principal) {
        if (originRefused(origin)) return ResponseEntity.status(HttpStatus.FORBIDDEN).build();
        if (sse == null) return ResponseEntity.status(HttpStatus.METHOD_NOT_ALLOWED).build();
        var emitter = new SseEmitter(0L);   // no servlet timeout: the stream ends when either side closes it
        var opened = FoundgineMcpSseTransport.get(sessions, sse, transport, origin, sessionId,
                principal == null ? null : principal.getName(), lastEventId, new EmitterSink(emitter), writer);
        if (opened.status() != 200) return ResponseEntity.status(opened.status()).build();
        var stream = opened.stream();
        open.put(sessionId, stream);
        var beat = heartbeats.scheduleWithFixedDelay(stream::heartbeat, sseOptions.heartbeatSeconds(), sseOptions.heartbeatSeconds(), TimeUnit.SECONDS);
        Runnable cleanup = () -> {
            beat.cancel(false);
            open.remove(sessionId, stream);
            stream.close();
        };
        emitter.onCompletion(cleanup);
        emitter.onTimeout(cleanup);
        emitter.onError(e -> cleanup.run());
        stream.start();
        return ResponseEntity.ok().header("Cache-Control", "no-cache").body(emitter);
    }

    /** Frames are already complete SSE text, so they are written raw rather than re-framed by the emitter. */
    private record EmitterSink(SseEmitter emitter) implements FoundgineMcpSseStream.Sink {
        @Override public void write(String frame) throws IOException { emitter.send(frame); }
        @Override public void complete() { emitter.complete(); }
    }

    /** Stops the heartbeat thread and closes every open stream (Spring calls this when the bean is destroyed). */
    public void close() {
        heartbeats.shutdownNow();
        open.values().forEach(x -> x.close(FoundgineMcpSseStream.CloseReason.SHUTDOWN));
        open.clear();
    }

    /** Terminates the caller's session. Without sessions enabled the endpoint has no DELETE (405). */
    @DeleteMapping
    public ResponseEntity<Void> delete(
            @RequestHeader(value = "Origin", required = false) String origin,
            @RequestHeader(value = FoundgineMcpSessionManager.HEADER_NAME, required = false) String sessionId,
            Principal principal) {
        if (originRefused(origin)) return ResponseEntity.status(HttpStatus.FORBIDDEN).build();
        if (sessions == null) return ResponseEntity.status(HttpStatus.METHOD_NOT_ALLOWED).build();
        int status = FoundgineMcpSseTransport.delete(sessions, sse, transport, origin, sessionId, principal == null ? null : principal.getName());
        if (status == 204) open.remove(sessionId);
        return ResponseEntity.status(status).build();
    }

    private boolean originRefused(String origin) { return FoundgineMcpSseTransport.originRefused(allowedOrigins, origin); }

    /** The JSON-RPC method of a single request, or {@code null} for anything else (batches, notifications without one, junk). */
    private static String methodOf(String body) {
        try {
            JsonNode node = JSON.readTree(body);
            return node != null && node.path("method").isTextual() ? node.get("method").asText() : null;
        } catch (Exception e) {
            return null;
        }
    }
}
