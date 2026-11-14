package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;

import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.Signature;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.atomic.AtomicReference;
import org.junit.jupiter.api.Test;

class McpConcurrencyTest {
    private static final int THREADS = 16;

    /** Runs {@code task(i)} on THREADS threads released together; rethrows the first failure. */
    private static void race(java.util.function.IntConsumer task) throws Exception {
        ExecutorService pool = Executors.newFixedThreadPool(THREADS);
        try {
            var go = new CountDownLatch(1);
            List<Future<?>> fs = new ArrayList<>();
            for (int i = 0; i < THREADS; i++) {
                int n = i;
                fs.add(pool.submit(() -> { go.await(); task.accept(n); return null; }));
            }
            go.countDown();
            for (var f : fs) f.get(60, TimeUnit.SECONDS);
        } finally { pool.shutdownNow(); }
    }

    @Test void sessionCapIsExactUnderContentionAndIdsStayUnique() throws Exception {
        int cap = 500;
        var m = new FoundgineMcpSessionManager(new FoundgineMcpSessionManager.Options(Duration.ofMinutes(10), Duration.ofHours(1), cap),
                new FoundgineMcpSessionManager.InMemoryStore(), System::currentTimeMillis);
        Set<String> ids = ConcurrentHashMap.newKeySet();
        var refused = new AtomicInteger();
        race(i -> {
            for (int k = 0; k < 100; k++) {
                try { assertTrue(ids.add(m.create(null).sessionId()), "duplicate session id"); }
                catch (IllegalStateException full) { refused.incrementAndGet(); }
            }
        });
        assertEquals(cap, ids.size());
        assertEquals(cap, m.activeSessionCount());
        assertEquals(THREADS * 100 - cap, refused.get());
    }

    @Test void exactlyOneConcurrentTerminateWinsAndValidationNeverRevivesIt() throws Exception {
        var m = new FoundgineMcpSessionManager();
        for (int round = 0; round < 50; round++) {
            String id = m.create("alice").sessionId();
            var wins = new AtomicInteger();
            race(i -> {
                if (i % 2 == 0) { if (m.terminate(id, "alice")) wins.incrementAndGet(); }
                else m.validate(id, "alice");
            });
            assertEquals(1, wins.get());
            assertFalse(m.validate(id, "alice").isValid());
        }
        assertEquals(0, m.activeSessionCount());
    }

    @Test void expiryAndPurgeRaceSafely() throws Exception {
        var now = new AtomicLong(1_000);
        var m = new FoundgineMcpSessionManager(new FoundgineMcpSessionManager.Options(Duration.ofMillis(50), Duration.ofMillis(200), 100_000),
                new FoundgineMcpSessionManager.InMemoryStore(), now::get);
        var removed = new AtomicInteger();
        var created = new AtomicInteger();
        race(i -> {
            for (int k = 0; k < 200; k++) {
                if (i % 4 == 0) now.addAndGet(7);
                else if (i % 4 == 1) removed.addAndGet(m.purgeExpired());
                else { var s = m.create(null); created.incrementAndGet(); m.validate(s.sessionId(), null); }
            }
        });
        now.addAndGet(10_000);
        removed.addAndGet(m.purgeExpired());
        assertEquals(0, m.activeSessionCount());
        assertTrue(removed.get() <= created.get());
    }

    @Test void rotationRevocationAndVerificationNeverObserveATornKeySet() throws Exception {
        List<KeyPair> pairs = new ArrayList<>();
        var gen = KeyPairGenerator.getInstance("RSA");
        gen.initialize(2048);
        for (int i = 0; i < 6; i++) pairs.add(gen.generateKeyPair());
        var ring = new FoundgineMcpApprovalKeyRing(Duration.ofSeconds(1));
        ring.activateSigningKey("k0", pairs.get(0).getPrivate(), pairs.get(0).getPublic());
        var stop = new java.util.concurrent.atomic.AtomicBoolean();
        var failure = new AtomicReference<Throwable>();
        var verified = new AtomicInteger();
        byte[] payload = "payload".getBytes();

        ExecutorService pool = Executors.newFixedThreadPool(THREADS);
        try {
            List<Future<?>> readers = new ArrayList<>();
            for (int r = 0; r < THREADS - 1; r++) readers.add(pool.submit(() -> {
                while (!stop.get() && failure.get() == null) {
                    try {
                        var signer = ring.snapshot();                 // id and key fixed together
                        byte[] sig = signer.sign(payload);
                        var pub = ring.resolve(signer.keyId());      // retained for an hour: must still resolve
                        if (pub == null) throw new AssertionError("a retained key stopped resolving: " + signer.keyId());
                        var v = Signature.getInstance("SHA256withRSA");
                        v.initVerify(pub); v.update(payload);
                        if (!v.verify(sig)) throw new AssertionError("signature does not verify under its own key id");
                        verified.incrementAndGet();
                    } catch (Throwable t) { failure.compareAndSet(null, t); }
                }
            }));
            for (int i = 1; i < pairs.size(); i++) {
                ring.activateSigningKey("k" + i, pairs.get(i).getPrivate(), pairs.get(i).getPublic(), Duration.ofHours(1));
                Thread.sleep(20);
            }
            stop.set(true);
            for (var f : readers) f.get(60, TimeUnit.SECONDS);
        } finally { pool.shutdownNow(); }
        if (failure.get() != null) throw new AssertionError(failure.get());
        assertTrue(verified.get() > 0);
        assertEquals("k5", ring.keyId());
        assertEquals(6, ring.keys().size());
        assertTrue(ring.revoke("k5"));
        assertNull(ring.resolve("k5"));
    }
}
