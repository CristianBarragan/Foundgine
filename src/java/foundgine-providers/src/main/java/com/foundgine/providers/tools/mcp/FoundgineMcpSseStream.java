package com.foundgine.providers.tools.mcp;

import java.io.IOException;
import java.util.Locale;
import java.util.Objects;
import java.util.concurrent.Executor;
import java.util.function.LongSupplier;

/**
 * Transport-neutral live SSE connection over one session's {@link FoundgineMcpSseBuffer}. The transport supplies a
 * {@link Sink} (e.g. a Spring {@code SseEmitter}) and calls {@link #start()}; replay of the retained events after
 * {@code Last-Event-ID} and every later event then flow through one path: the stream keeps a cursor (the id of the last event
 * it wrote) and, whenever the buffer nudges it, writes {@code replayAfter(cursor)}. Events therefore arrive in order, exactly
 * once, with no gap between replay and live.
 *
 * <p><b>Limits.</b> Only one stream may be open per session ({@link OpenStatus#BUSY}); {@link FoundgineMcpSseLimits} caps
 * streams in total and per principal ({@link OpenStatus#LIMIT}); a stream older than {@link Options#maxLifetimeMillis} is
 * closed at its next heartbeat so the client reconnects with {@code Last-Event-ID} (the session's own idle deadline is not
 * extended by an open stream). If the client falls so far behind that the buffer evicts events it has not yet received, the
 * stream closes rather than skip them; a write failure (client gone) closes it; retiring the buffer (session ended) closes
 * it. Writes happen on the supplied {@link Executor}, never under the buffer's lock, so a slow client cannot block a
 * publisher. {@link #close} is idempotent and safe from any thread.
 *
 * <p><b>Audit.</b> {@code SSE_OPENED}, {@code SSE_REJECTED} and {@code SSE_CLOSED} events carry the session fingerprint and
 * the principal, never the session id, a payload or a {@code Last-Event-ID}.
 */
public final class FoundgineMcpSseStream implements AutoCloseable {
    /** Where frames go. {@code write} may block; both methods are called from one thread at a time. */
    public interface Sink {
        void write(String frame) throws IOException;
        void complete();
    }

    /** {@code GONE}: the session's buffer was retired (the session ended) before or while the stream opened. */
    public enum OpenStatus { OK, EVICTED, INVALID, BUSY, LIMIT, GONE }

    /** Why a stream ended, as recorded in the {@code SSE_CLOSED} audit event ({@code reason=<lower-case name>}). */
    public enum CloseReason { CLIENT_CLOSED, WRITE_FAILED, EVICTED, SESSION_ENDED, LIFETIME, SHUTDOWN }

    /**
     * @param sessionId the admitted session (used only for the audit fingerprint)
     * @param owner the authenticated principal, for the per-principal limit and the audit trail; may be {@code null}
     * @param maxLifetimeMillis how long a stream may stay open; {@code <= 0} for no limit
     */
    public record Options(String sessionId, String owner, FoundgineMcpSseLimits limits, long maxLifetimeMillis,
                          FoundgineMcpAuditSink audit, LongSupplier clock) {
        public Options {
            limits = limits == null ? FoundgineMcpSseLimits.UNLIMITED : limits;
            audit = audit == null ? FoundgineMcpAuditSink.NONE : audit;
            clock = clock == null ? System::currentTimeMillis : clock;
        }

        /** No limits, no lifetime cap, no audit. */
        public static Options none() { return new Options(null, null, null, 0, null, null); }
    }

    /** @param stream non-null only when {@code status} is {@link OpenStatus#OK} */
    public record Opened(OpenStatus status, FoundgineMcpSseStream stream) {}

    private static final String KEEP_ALIVE = ": keep-alive\n\n";

    private final FoundgineMcpSseBuffer buffer;
    private final FoundgineMcpSseBuffer.Subscription subscription;
    private final FoundgineMcpSseLimits.Permit permit;
    private final Sink sink;
    private final Options options;
    private final long openedAt;
    private final boolean resume;
    private final Object drainLock = new Object();
    private String cursor;
    private boolean closed;
    private boolean started;

    private FoundgineMcpSseStream(FoundgineMcpSseBuffer buffer, FoundgineMcpSseBuffer.Subscription subscription,
            FoundgineMcpSseLimits.Permit permit, Sink sink, Options options, String cursor, boolean resume) {
        this.buffer = buffer;
        this.subscription = subscription;
        this.permit = permit;
        this.sink = sink;
        this.options = options;
        this.cursor = cursor;
        this.resume = resume;
        this.openedAt = options.clock().getAsLong();
    }

    /** Opens without limits or audit. */
    public static Opened open(FoundgineMcpSseBuffer buffer, String lastEventId, Sink sink, Executor executor) {
        return open(buffer, lastEventId, sink, executor, Options.none());
    }

    /**
     * Claims a stream permit and the session's stream slot, and checks {@code lastEventId}. Nothing is written until
     * {@link #start()}, so the transport can still answer with a plain error status.
     *
     * @param lastEventId the raw {@code Last-Event-ID}, or null/blank to receive only events appended from now on
     */
    public static Opened open(FoundgineMcpSseBuffer buffer, String lastEventId, Sink sink, Executor executor, Options options) {
        Objects.requireNonNull(buffer, "buffer");
        Objects.requireNonNull(sink, "sink");
        Objects.requireNonNull(executor, "executor");
        Objects.requireNonNull(options, "options");
        var permit = options.limits().tryAcquire(options.owner());
        if (permit == null) return rejected(options, OpenStatus.LIMIT);
        FoundgineMcpSseStream[] self = new FoundgineMcpSseStream[1];
        var subscription = buffer.subscribe(
                () -> {
                    var s = self[0];
                    if (s != null) {
                        try { executor.execute(s::drain); } catch (RuntimeException rejectedTask) { s.close(CloseReason.SHUTDOWN); }
                    }
                },
                () -> {   // off the caller's thread: closing waits for any write in progress to finish
                    var s = self[0];
                    if (s == null) return;
                    try { executor.execute(() -> s.close(CloseReason.SESSION_ENDED)); }
                    catch (RuntimeException rejectedTask) { s.close(CloseReason.SESSION_ENDED); }
                });
        if (subscription == null) {
            permit.close();
            return rejected(options, buffer.isRetired() ? OpenStatus.GONE : OpenStatus.BUSY);
        }
        var probe = buffer.replayAfter(lastEventId);
        if (probe.status() != FoundgineMcpSseBuffer.ReplayStatus.OK) {
            subscription.close();
            permit.close();
            return rejected(options, probe.status() == FoundgineMcpSseBuffer.ReplayStatus.EVICTED ? OpenStatus.EVICTED : OpenStatus.INVALID);
        }
        boolean none = lastEventId == null || lastEventId.chars().allMatch(c -> c == ' ' || c == '\t');
        String cursor = none ? Long.toString(buffer.lastSequence()) : lastEventId;
        self[0] = new FoundgineMcpSseStream(buffer, subscription, permit, sink, options, cursor, !none);
        if (buffer.isRetired()) {   // retired between claiming the slot and now: the retire callback saw no stream yet
            subscription.close();
            permit.close();
            return rejected(options, OpenStatus.GONE);
        }
        return new Opened(OpenStatus.OK, self[0]);
    }

    private static Opened rejected(Options options, OpenStatus status) {
        emit(options, FoundgineMcpAuditEvent.Type.SSE_REJECTED, "status=" + status.name());
        return new Opened(status, null);
    }

    private static void emit(Options o, FoundgineMcpAuditEvent.Type type, String detail) {
        FoundgineMcpAuditSink.emit(o.audit(), new FoundgineMcpAuditEvent(type, o.clock().getAsLong(),
                FoundgineMcpSessionManager.fingerprint(o.sessionId()), o.owner(), detail));
    }

    /** Writes the replay (and anything already appended) on the calling thread; later events follow on the executor. */
    public void start() {
        synchronized (drainLock) {
            if (closed || started) return;
            started = true;
        }
        emit(options, FoundgineMcpAuditEvent.Type.SSE_OPENED, "resume=" + resume);
        drain();
    }

    /** Writes an SSE comment so idle proxies keep the connection open, and ends a stream past its maximum lifetime. */
    public void heartbeat() {
        synchronized (drainLock) {
            if (closed) return;
            if (options.maxLifetimeMillis() > 0 && options.clock().getAsLong() - openedAt >= options.maxLifetimeMillis()) {
                close(CloseReason.LIFETIME);
                return;
            }
            try { sink.write(KEEP_ALIVE); } catch (IOException | RuntimeException e) { close(CloseReason.WRITE_FAILED); }
        }
    }

    private void drain() {
        synchronized (drainLock) {
            if (closed) return;
            var replay = buffer.replayAfter(cursor);
            if (replay.status() != FoundgineMcpSseBuffer.ReplayStatus.OK) { close(CloseReason.EVICTED); return; }
            for (var e : replay.events()) {
                try { sink.write(FoundgineMcpSseBuffer.format(e)); } catch (IOException | RuntimeException failed) { close(CloseReason.WRITE_FAILED); return; }
                cursor = e.id();
            }
        }
    }

    public boolean isClosed() { synchronized (drainLock) { return closed; } }

    /** Closes the stream because the client went away (the transport's completion callback). */
    @Override public void close() { close(CloseReason.CLIENT_CLOSED); }

    /** Closes the stream for the given reason; only the first close is recorded. */
    public void close(CloseReason reason) {
        synchronized (drainLock) {
            if (closed) return;
            closed = true;
        }
        subscription.close();
        permit.close();
        try { sink.complete(); } catch (RuntimeException ignored) { /* already gone */ }
        emit(options, FoundgineMcpAuditEvent.Type.SSE_CLOSED, "reason=" + reason.name().toLowerCase(Locale.ROOT));
    }
}
