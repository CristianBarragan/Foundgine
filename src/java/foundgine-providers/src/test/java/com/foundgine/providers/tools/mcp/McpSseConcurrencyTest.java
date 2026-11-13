package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.Options;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.function.IntConsumer;
import org.junit.jupiter.api.Test;

/** Contention tests for the SSE buffer, registry and transport. Threads are released together; each test has a hard timeout. */
class McpSseConcurrencyTest {
    private static final int THREADS = 16;

    private static void race(IntConsumer task) throws Exception {
        ExecutorService pool = Executors.newFixedThreadPool(THREADS);
        try {
            var go = new CountDownLatch(1);
            List<Future<?>> done = new ArrayList<>();
            for (int i = 0; i < THREADS; i++) {
                final int n = i;
                done.add(pool.submit(() -> { go.await(); task.accept(n); return null; }));
            }
            go.countDown();
            for (var f : done) f.get(60, TimeUnit.SECONDS);   // rethrows the first failure
        } finally {
            pool.shutdownNow();
        }
    }

    private static final FoundgineMcpSseStream.Sink NOOP = new FoundgineMcpSseStream.Sink() {
        @Override public void write(String frame) {}
        @Override public void complete() {}
    };

    @Test void appendDuringReplayNeverShowsAGapOrADuplicate() throws Exception {
        final int perThread = 200;
        var buffer = new FoundgineMcpSseBuffer(100_000, 100_000_000L);
        var ids = ConcurrentHashMap.<String>newKeySet();
        race(n -> {
            if (n % 2 == 0) {
                for (int i = 0; i < perThread; i++) assertTrue(ids.add(buffer.append("m" + n + "-" + i).id()), "duplicate id");
            } else {
                for (int i = 0; i < perThread; i++) {
                    var replay = buffer.replayAfter("0");
                    assertEquals(FoundgineMcpSseBuffer.ReplayStatus.OK, replay.status());
                    long expected = 1;
                    for (var e : replay.events()) assertEquals(expected++, e.sequence(), "replay must be gapless and ascending");
                }
            }
        });
        assertEquals((long) (THREADS / 2) * perThread, buffer.lastSequence());
        assertEquals(buffer.lastSequence(), ids.size());
    }

    @Test void lruHoldsItsBoundAndRetiresWhatItEvictsUnderContention() throws Exception {
        final int max = 8;
        var registry = new FoundgineMcpSseRegistry(max, 4, 1000);
        var seen = new ConcurrentHashMap<String, FoundgineMcpSseBuffer>();
        race(n -> {
            for (int i = 0; i < 300; i++) {
                String id = ("s" + ((n * 31 + i * 7) % 64) + "x".repeat(43)).substring(0, 43);   // must be a well-formed 43-char id
                var b = registry.stream(id);
                assertNotNull(b);
                seen.put(id + "#" + System.identityHashCode(b), b);
                assertTrue(registry.count() <= max, "bound exceeded");
            }
        });
        assertEquals(max, registry.count());
        long live = seen.values().stream().filter(b -> !b.isRetired()).count();
        assertEquals(max, live, "exactly the buffers still in the registry are unretired");
    }

    @Test void simultaneousGetsOnOneSessionYieldOneStreamAndTheRestAreBusy() throws Exception {
        var gate = new FoundgineMcpSessionGate(new FoundgineMcpSessionManager(
                new Options(Duration.ofMinutes(10), Duration.ofHours(1), 100), new FoundgineMcpSessionManager.InMemoryStore(), System::currentTimeMillis));
        var registry = new FoundgineMcpSseRegistry();
        var settings = new FoundgineMcpSseTransport.Settings(List.of(), 0, new FoundgineMcpSseLimits(1000, 1000), FoundgineMcpAuditSink.NONE);
        String id = gate.admitPost("initialize", null, "alice").issuedSessionId();
        var ok = new AtomicInteger();
        var busy = new AtomicInteger();
        var streams = new java.util.concurrent.ConcurrentLinkedQueue<FoundgineMcpSseStream>();
        race(n -> {
            var r = FoundgineMcpSseTransport.get(gate, registry, settings, null, id, "alice", null, NOOP, Runnable::run);
            if (r.status() == 200) { ok.incrementAndGet(); streams.add(r.stream()); }
            else if (r.status() == 409) busy.incrementAndGet();
            else fail("unexpected status " + r.status());
        });
        assertEquals(1, ok.get());
        assertEquals(THREADS - 1, busy.get());
        streams.forEach(FoundgineMcpSseStream::close);
    }

    @Test void perPrincipalLimitIsExactUnderContention() throws Exception {
        final int cap = 3;
        var gate = new FoundgineMcpSessionGate(new FoundgineMcpSessionManager(
                new Options(Duration.ofMinutes(10), Duration.ofHours(1), 100), new FoundgineMcpSessionManager.InMemoryStore(), System::currentTimeMillis));
        var registry = new FoundgineMcpSseRegistry();
        var settings = new FoundgineMcpSseTransport.Settings(List.of(), 0, new FoundgineMcpSseLimits(1000, cap), FoundgineMcpAuditSink.NONE);
        String[] sessions = new String[THREADS];
        for (int i = 0; i < THREADS; i++) sessions[i] = gate.admitPost("initialize", null, "alice").issuedSessionId();
        var ok = new AtomicInteger();
        var limited = new AtomicInteger();
        var streams = new java.util.concurrent.ConcurrentLinkedQueue<FoundgineMcpSseStream>();
        race(n -> {
            var r = FoundgineMcpSseTransport.get(gate, registry, settings, null, sessions[n], "alice", null, NOOP, Runnable::run);
            if (r.status() == 200) { ok.incrementAndGet(); streams.add(r.stream()); }
            else if (r.status() == 429) limited.incrementAndGet();
            else fail("unexpected status " + r.status());
        });
        assertEquals(cap, ok.get());
        assertEquals(THREADS - cap, limited.get());
        streams.forEach(FoundgineMcpSseStream::close);
    }
}
