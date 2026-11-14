package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.providers.tools.mcp.FoundgineMcpSseStream.OpenStatus;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;
import org.junit.jupiter.api.Test;

class McpSseStreamTest {
    private static final class Recorder implements FoundgineMcpSseStream.Sink {
        final List<String> frames = Collections.synchronizedList(new ArrayList<>());
        final AtomicBoolean completed = new AtomicBoolean();
        volatile boolean failWrites;
        @Override public void write(String frame) throws IOException {
            if (failWrites) throw new IOException("client gone");
            frames.add(frame);
        }
        @Override public void complete() { completed.set(true); }
    }

    private static String frame(long id, String data) { return "id: " + id + "\ndata: " + data + "\n\n"; }

    @Test void replaysAfterLastEventIdThenDeliversLiveEventsInOrderExactlyOnce() {
        var buffer = new FoundgineMcpSseBuffer(10, 10_000);
        buffer.append("a"); buffer.append("b");
        var sink = new Recorder();
        var opened = FoundgineMcpSseStream.open(buffer, "1", sink, Runnable::run);
        assertEquals(OpenStatus.OK, opened.status());
        opened.stream().start();
        assertEquals(List.of(frame(2, "b")), sink.frames);
        buffer.append("c"); buffer.append("d");
        assertEquals(List.of(frame(2, "b"), frame(3, "c"), frame(4, "d")), sink.frames);
    }

    @Test void noLastEventIdMeansOnlyNewEvents() {
        var buffer = new FoundgineMcpSseBuffer(10, 10_000);
        buffer.append("old");
        var sink = new Recorder();
        var opened = FoundgineMcpSseStream.open(buffer, null, sink, Runnable::run);
        opened.stream().start();
        assertTrue(sink.frames.isEmpty());
        buffer.append("new");
        assertEquals(List.of(frame(2, "new")), sink.frames);
    }

    @Test void onlyOneStreamPerSessionAndFailedOpensFreeTheSlot() {
        var buffer = new FoundgineMcpSseBuffer(2, 10_000);
        buffer.append("a"); buffer.append("b"); buffer.append("c");   // keeps b, c
        var sink = new Recorder();
        assertEquals(OpenStatus.EVICTED, FoundgineMcpSseStream.open(buffer, "0", sink, Runnable::run).status());
        assertEquals(OpenStatus.INVALID, FoundgineMcpSseStream.open(buffer, "9", sink, Runnable::run).status());
        var first = FoundgineMcpSseStream.open(buffer, null, sink, Runnable::run);
        assertEquals(OpenStatus.OK, first.status());
        assertEquals(OpenStatus.BUSY, FoundgineMcpSseStream.open(buffer, null, sink, Runnable::run).status());
        first.stream().close();
        assertEquals(OpenStatus.OK, FoundgineMcpSseStream.open(buffer, null, sink, Runnable::run).status());
    }

    @Test void closeIsIdempotentCompletesTheSinkAndStopsDelivery() {
        var buffer = new FoundgineMcpSseBuffer(10, 10_000);
        var sink = new Recorder();
        var stream = FoundgineMcpSseStream.open(buffer, null, sink, Runnable::run).stream();
        stream.start();
        stream.close();
        stream.close();
        assertTrue(stream.isClosed());
        assertTrue(sink.completed.get());
        buffer.append("late");
        assertTrue(sink.frames.isEmpty());
    }

    @Test void aWriteFailureClosesTheStreamWithoutFailingThePublisher() {
        var buffer = new FoundgineMcpSseBuffer(10, 10_000);
        var sink = new Recorder();
        var stream = FoundgineMcpSseStream.open(buffer, null, sink, Runnable::run).stream();
        stream.start();
        sink.failWrites = true;
        assertDoesNotThrow(() -> buffer.append("x"));
        assertTrue(stream.isClosed());
        assertTrue(sink.completed.get());
    }

    @Test void aClientTooSlowToKeepUpIsClosedRatherThanSkipped() {
        var buffer = new FoundgineMcpSseBuffer(2, 10_000);
        var sink = new Recorder();
        var queued = new ArrayList<Runnable>();
        var stream = FoundgineMcpSseStream.open(buffer, null, sink, queued::add).stream();
        stream.start();
        buffer.append("1"); buffer.append("2"); buffer.append("3");   // 1 is evicted before the writer ever ran
        queued.forEach(Runnable::run);
        assertTrue(stream.isClosed());
        assertTrue(sink.frames.isEmpty());   // never delivers 2 and 3 while silently skipping 1
    }

    @Test void heartbeatWritesACommentAndStopsAfterClose() {
        var buffer = new FoundgineMcpSseBuffer(10, 10_000);
        var sink = new Recorder();
        var stream = FoundgineMcpSseStream.open(buffer, null, sink, Runnable::run).stream();
        stream.heartbeat();
        assertEquals(List.of(": keep-alive\n\n"), sink.frames);
        stream.close();
        stream.heartbeat();
        assertEquals(1, sink.frames.size());
    }

    @Test void publisherDeliversOnlyToSessionsThatHaveAStream() {
        var registry = new FoundgineMcpSseRegistry();
        var publisher = new FoundgineMcpSsePublisher(registry);
        String id = "A".repeat(43);
        String note = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}";
        assertTrue(publisher.publish(id, note).isEmpty());          // no buffer: dropped, nothing allocated
        assertEquals(0, registry.count());
        registry.stream(id);
        var sink = new Recorder();
        FoundgineMcpSseStream.open(registry.find(id), null, sink, Runnable::run).stream().start();
        var event = publisher.publish(id, "{ \"jsonrpc\": \"2.0\",\n \"method\": \"notifications/tools/list_changed\" }").orElseThrow();
        assertEquals(note, event.data());                           // re-serialised to one compact line
        assertEquals(List.of(frame(1, note)), sink.frames);
    }

    @Test void publisherRefusesAnythingButASingleJsonRpcMessage() {
        var registry = new FoundgineMcpSseRegistry();
        var publisher = new FoundgineMcpSsePublisher(registry);
        String id = "A".repeat(43);
        registry.stream(id);
        for (String bad : new String[] {"[]", "{}", "nope", "{\"jsonrpc\":\"1.0\",\"method\":\"x\"}", "{\"jsonrpc\":\"2.0\"}",
                "[{\"jsonrpc\":\"2.0\",\"method\":\"x\"}]", "{\"jsonrpc\":\"2.0\",\"id\":1}"}) {
            assertThrows(IllegalArgumentException.class, () -> publisher.publish(id, bad), bad);
        }
        assertEquals(0, registry.find(id).lastSequence());
        assertTrue(publisher.publish(id, "{\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{}}").isPresent());   // a response is allowed
    }

    // ---- Tier 2: limits, lifetime, retirement and audit

    private static final String SESSION = "A".repeat(43);

    private static FoundgineMcpSseStream.Options options(FoundgineMcpSseLimits limits, long lifetime, List<FoundgineMcpAuditEvent> log,
            java.util.function.LongSupplier clock) {
        return new FoundgineMcpSseStream.Options(SESSION, "alice", limits, lifetime, log::add, clock);
    }

    @Test void streamLimitsAreEnforcedPerPrincipalAndReleasedOnClose() {
        var limits = new FoundgineMcpSseLimits(10, 1);
        var log = Collections.synchronizedList(new ArrayList<FoundgineMcpAuditEvent>());
        var one = FoundgineMcpSseStream.open(new FoundgineMcpSseBuffer(5, 1000), null, new Recorder(), Runnable::run, options(limits, 0, log, null));
        assertEquals(OpenStatus.OK, one.status());
        var refused = FoundgineMcpSseStream.open(new FoundgineMcpSseBuffer(5, 1000), null, new Recorder(), Runnable::run, options(limits, 0, log, null));
        assertEquals(OpenStatus.LIMIT, refused.status());
        assertEquals(1, limits.open());
        one.stream().close();
        assertEquals(0, limits.open());
        assertEquals(OpenStatus.OK, FoundgineMcpSseStream.open(new FoundgineMcpSseBuffer(5, 1000), null, new Recorder(), Runnable::run, options(limits, 0, log, null)).status());
    }

    @Test void aRefusedOpenReleasesItsPermit() {
        var limits = new FoundgineMcpSseLimits(10, 10);
        var buffer = new FoundgineMcpSseBuffer(5, 1000);
        var first = FoundgineMcpSseStream.open(buffer, null, new Recorder(), Runnable::run, options(limits, 0, new ArrayList<>(), null));
        assertEquals(OpenStatus.BUSY, FoundgineMcpSseStream.open(buffer, null, new Recorder(), Runnable::run, options(limits, 0, new ArrayList<>(), null)).status());
        assertEquals(OpenStatus.INVALID, FoundgineMcpSseStream.open(new FoundgineMcpSseBuffer(5, 1000), "9", new Recorder(), Runnable::run, options(limits, 0, new ArrayList<>(), null)).status());
        assertEquals(1, limits.open());   // only the first stream holds a permit
        first.stream().close();
        assertEquals(0, limits.open());
    }

    @Test void aStreamPastItsMaximumLifetimeEndsAtTheNextHeartbeat() {
        var now = new java.util.concurrent.atomic.AtomicLong(0);
        var log = new ArrayList<FoundgineMcpAuditEvent>();
        var sink = new Recorder();
        var stream = FoundgineMcpSseStream.open(new FoundgineMcpSseBuffer(5, 1000), null, sink, Runnable::run,
                options(null, 1000, log, now::get)).stream();
        stream.start();
        now.set(999);
        stream.heartbeat();
        assertFalse(stream.isClosed());
        now.set(1000);
        stream.heartbeat();
        assertTrue(stream.isClosed());
        assertTrue(sink.completed.get());
        assertEquals("reason=lifetime", log.get(log.size() - 1).detail());
    }

    @Test void retiringTheBufferClosesTheStreamAndFreesItsPermit() {
        var limits = new FoundgineMcpSseLimits(10, 10);
        var log = new ArrayList<FoundgineMcpAuditEvent>();
        var buffer = new FoundgineMcpSseBuffer(5, 1000);
        var sink = new Recorder();
        var stream = FoundgineMcpSseStream.open(buffer, null, sink, Runnable::run, options(limits, 0, log, null)).stream();
        stream.start();
        buffer.retire();
        assertTrue(stream.isClosed());
        assertTrue(sink.completed.get());
        assertEquals(0, limits.open());
        assertEquals("reason=session_ended", log.get(log.size() - 1).detail());
        assertEquals(OpenStatus.GONE, FoundgineMcpSseStream.open(buffer, null, new Recorder(), Runnable::run, options(limits, 0, log, null)).status());
    }

    @Test void auditEventsCarryTheFingerprintNeverTheSessionIdAndOnlyTheFirstCloseIsRecorded() {
        var log = new ArrayList<FoundgineMcpAuditEvent>();
        var buffer = new FoundgineMcpSseBuffer(5, 1000);
        buffer.append("a");
        var stream = FoundgineMcpSseStream.open(buffer, "1", new Recorder(), Runnable::run, options(null, 0, log, null)).stream();
        stream.start();
        stream.start();   // a second start neither re-opens nor re-audits
        stream.close();
        stream.close(FoundgineMcpSseStream.CloseReason.SHUTDOWN);
        assertEquals(List.of(FoundgineMcpAuditEvent.Type.SSE_OPENED, FoundgineMcpAuditEvent.Type.SSE_CLOSED),
                log.stream().map(FoundgineMcpAuditEvent::type).toList());
        assertEquals("resume=true", log.get(0).detail());
        assertEquals("reason=client_closed", log.get(1).detail());
        for (var e : log) {
            assertEquals(FoundgineMcpSessionManager.fingerprint(SESSION), e.subject());
            assertEquals("alice", e.actor());
            assertFalse(String.valueOf(e.subject()).contains(SESSION));
        }
    }

    @Test void aThrowingAuditSinkNeverBreaksTheStream() {
        var options = new FoundgineMcpSseStream.Options(SESSION, "alice", null, 0, e -> { throw new IllegalStateException("boom"); }, null);
        var opened = FoundgineMcpSseStream.open(new FoundgineMcpSseBuffer(5, 1000), null, new Recorder(), Runnable::run, options);
        assertEquals(OpenStatus.OK, opened.status());
        assertDoesNotThrow(() -> { opened.stream().start(); opened.stream().close(); });
    }

    @Test void limitsValidateAndUnboundSessionsShareOneBucket() {
        assertThrows(IllegalArgumentException.class, () -> new FoundgineMcpSseLimits(0, 1));
        assertThrows(IllegalArgumentException.class, () -> new FoundgineMcpSseLimits(1, 0));
        var limits = new FoundgineMcpSseLimits(5, 1);
        var p = limits.tryAcquire(null);
        assertNotNull(p);
        assertNull(limits.tryAcquire(null));
        assertNotNull(limits.tryAcquire("bob"));
        p.close(); p.close();   // idempotent: a double close must not free a second slot
        assertEquals(1, limits.open());
        assertNotNull(limits.tryAcquire(null));
    }
}
