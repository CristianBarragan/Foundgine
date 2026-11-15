package com.foundgine.providers.tools.mcp.redis;

import static org.junit.jupiter.api.Assertions.*;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager;
import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.Options;
import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.Status;
import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.StoredSession;
import java.time.Duration;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;

/**
 * Store contract plus "two replicas" scenarios. The failure-path tests always run; the rest need a real server and run when
 * {@code FOUNDGINE_REDIS=host:port} is set (they are skipped otherwise).
 */
class RedisMcpSessionStoreTest {
    private static final long NOW = 1_800_000_000_000L;

    private static RespRedis server() {
        RespRedis r = RespRedis.fromEnvironment();
        assumeTrue(r != null, "FOUNDGINE_REDIS is not set");
        return r;
    }

    private static RedisMcpSessionStore freshStore(RespRedis r) {
        return new RedisMcpSessionStore(r, "{t" + System.nanoTime() + "}:sessions:");
    }

    private static StoredSession session(String key, String owner, long idle, long abs) {
        return new StoredSession(key, owner, NOW, NOW, idle, abs);
    }

    // ---- always run -------------------------------------------------------------------------------------------

    @Test
    void backendFailureSurfacesAsIllegalStateAndNeverAsASession() {
        var down = new RedisMcpSessionStore((script, keys, args) -> { throw new IllegalStateException("connection refused"); });
        assertThrows(IllegalStateException.class, () -> down.find("k"));
        assertThrows(IllegalStateException.class, () -> down.touch("k", 1, 2));
        assertThrows(IllegalStateException.class, () -> down.tryAdd(session("k", null, 10, 20)));
        assertThrows(IllegalStateException.class, () -> down.remove("k"));
        assertThrows(IllegalStateException.class, () -> down.removeExpired(1));
        assertThrows(IllegalStateException.class, down::count);
    }

    @Test
    void malformedRepliesAreRejected() {
        assertThrows(IllegalStateException.class, () -> new RedisMcpSessionStore((s, k, a) -> List.of("a", "b")).find("k"));
        assertThrows(IllegalStateException.class,
                () -> new RedisMcpSessionStore((s, k, a) -> List.of("", "x", "1", "2", "3")).find("k"));
        assertThrows(IllegalStateException.class, () -> new RedisMcpSessionStore((s, k, a) -> List.of()).count());
        assertEquals(null, new RedisMcpSessionStore((s, k, a) -> List.of()).find("k"));
    }

    @Test
    void blankPrefixIsRejected() {
        assertThrows(IllegalArgumentException.class, () -> new RedisMcpSessionStore((s, k, a) -> List.of(), " "));
    }

    // ---- real server ------------------------------------------------------------------------------------------

    @Test
    void addFindRoundTripAndCollision() {
        var store = freshStore(server());
        assertTrue(store.tryAdd(session("k1", "owner-digest", NOW + 1000, NOW + 5000)));
        assertFalse(store.tryAdd(session("k1", null, NOW + 9, NOW + 9)));
        assertEquals(session("k1", "owner-digest", NOW + 1000, NOW + 5000), store.find("k1"));
        assertTrue(store.tryAdd(session("k2", null, NOW + 1000, NOW + 5000)));
        assertEquals(null, store.find("k2").ownerDigest());
        assertEquals(null, store.find("missing"));
        assertEquals(2, store.count());
    }

    @Test
    void touchSlidesForwardNeverBackwardAndNeverCreates() {
        var store = freshStore(server());
        store.tryAdd(new StoredSession("k", null, NOW, NOW, NOW + 1000, NOW + 10_000));
        var up = store.touch("k", NOW + 500, NOW + 1500);
        assertEquals(NOW + 500, up.lastAccessedAtUnixMilliseconds());
        assertEquals(NOW + 1500, up.idleExpiresAtUnixMilliseconds());
        var back = store.touch("k", NOW + 100, NOW + 200);
        assertEquals(NOW + 500, back.lastAccessedAtUnixMilliseconds());
        assertEquals(NOW + 1500, back.idleExpiresAtUnixMilliseconds());
        assertEquals(NOW + 10_000, back.absoluteExpiresAtUnixMilliseconds());
        assertEquals(null, store.touch("nope", NOW, NOW + 1));
        assertEquals(null, store.find("nope"));
        assertEquals(1, store.count());
    }

    @Test
    void removeReportsWhetherItExisted() {
        var store = freshStore(server());
        store.tryAdd(session("k", null, NOW + 1000, NOW + 5000));
        assertTrue(store.remove("k"));
        assertFalse(store.remove("k"));
        assertEquals(null, store.find("k"));
        assertEquals(0, store.count());
    }

    @Test
    void removeExpiredUsesTheEarlierDeadlineInclusivelyAndKeepsTheRest() {
        var store = freshStore(server());
        store.tryAdd(session("idle-passed", null, NOW + 10, NOW + 9_000));
        store.tryAdd(session("abs-passed", null, NOW + 9_000, NOW + 20));
        store.tryAdd(session("exactly-now", null, NOW + 50, NOW + 9_000));
        store.tryAdd(session("live", null, NOW + 9_000, NOW + 9_000));
        assertEquals(4, store.count());   // expired sessions still count until purged
        assertEquals(3, store.removeExpired(NOW + 50));
        assertEquals(null, store.find("idle-passed"));
        assertEquals(null, store.find("abs-passed"));
        assertEquals(null, store.find("exactly-now"));
        assertEquals("live", store.find("live").key());
        assertEquals(1, store.count());
        assertEquals(0, store.removeExpired(NOW + 50));
    }

    @Test
    void removeExpiredHandlesMoreThanOneBatch() {
        var store = freshStore(server());
        for (int i = 0; i < 1200; i++) store.tryAdd(session("k" + i, null, NOW + 1, NOW + 2));
        assertEquals(1200, store.count());
        assertEquals(1200, store.removeExpired(NOW + 10));
        assertEquals(0, store.count());
    }

    @Test
    void hashesGetAServerSideExpiryOfAbsoluteDeadlinePlusGrace() {
        var r = server();
        String prefix = "{t" + System.nanoTime() + "}:sessions:";
        var store = new RedisMcpSessionStore(r, prefix);
        long abs = System.currentTimeMillis() + 120_000;
        store.tryAdd(new StoredSession("k", null, 1, 1, abs, abs));
        long pttl = (Long) r.command("PTTL", prefix + "s:k");
        assertTrue(pttl > 120_000 && pttl <= 180_000, "pttl=" + pttl);
    }

    @Test
    void unboundedLifetimeIsClampedForRedisButStoredIntact() {
        var r = server();
        String prefix = "{t" + System.nanoTime() + "}:sessions:";
        var store = new RedisMcpSessionStore(r, prefix);
        assertTrue(store.tryAdd(new StoredSession("k", null, NOW, NOW, Long.MAX_VALUE, Long.MAX_VALUE)));
        assertEquals(Long.MAX_VALUE, store.find("k").absoluteExpiresAtUnixMilliseconds());
        assertEquals(Long.MAX_VALUE, store.touch("k", NOW + 1, NOW + 2).idleExpiresAtUnixMilliseconds());
        assertTrue((Long) r.command("PTTL", prefix + "s:k") > 0);
    }

    @Test
    void exactlyOneOfManyConcurrentAddsWins() throws Exception {
        var store = freshStore(server());
        int threads = 32;
        var pool = Executors.newFixedThreadPool(threads);
        var start = new CountDownLatch(1);
        var winners = new AtomicInteger();
        for (int i = 0; i < threads; i++)
            pool.submit(() -> {
                start.await();
                if (store.tryAdd(session("same", null, NOW + 1, NOW + 2))) winners.incrementAndGet();
                return null;
            });
        start.countDown();
        pool.shutdown();
        assertTrue(pool.awaitTermination(20, TimeUnit.SECONDS));
        assertEquals(1, winners.get());
    }

    @Test
    void concurrentTouchesNeverMoveTheDeadlineBackwards() throws Exception {
        var store = freshStore(server());
        store.tryAdd(new StoredSession("k", null, NOW, NOW, NOW + 1, NOW + 1_000_000));
        int threads = 32;
        var pool = Executors.newFixedThreadPool(threads);
        var start = new CountDownLatch(1);
        for (int i = 1; i <= threads; i++) {
            final long t = i * 100L;
            pool.submit(() -> {
                start.await();
                store.touch("k", NOW + t, NOW + t);
                return null;
            });
        }
        start.countDown();
        pool.shutdown();
        assertTrue(pool.awaitTermination(20, TimeUnit.SECONDS));
        assertEquals(NOW + threads * 100L, store.find("k").idleExpiresAtUnixMilliseconds());
        assertEquals(NOW + threads * 100L, store.find("k").lastAccessedAtUnixMilliseconds());
    }

    @Test
    void sessionCreatedOnOneReplicaIsValidTerminatedAndOwnedAcrossReplicas() {
        var r = server();
        String prefix = "{t" + System.nanoTime() + "}:sessions:";
        long[] clock = {NOW};
        var options = new Options(Duration.ofMinutes(30), Duration.ofHours(24), 100);
        var replicaA = new FoundgineMcpSessionManager(options, new RedisMcpSessionStore(r, prefix), () -> clock[0]);
        var replicaB = new FoundgineMcpSessionManager(options, new RedisMcpSessionStore(r, prefix), () -> clock[0]);

        var s = replicaA.create("alice");
        assertTrue(replicaB.validate(s.sessionId(), "alice").isValid());
        assertEquals(Status.OWNER_MISMATCH, replicaB.validate(s.sessionId(), "mallory").status());
        assertEquals(Status.OWNER_MISMATCH, replicaB.validate(s.sessionId(), null).status());
        assertFalse(replicaB.terminate(s.sessionId(), "mallory"));
        assertTrue(replicaA.isLive(s.sessionId()));

        // the idle deadline slides on a different replica than the one that created the session
        clock[0] = NOW + Duration.ofMinutes(20).toMillis();
        assertTrue(replicaB.validate(s.sessionId(), "alice").isValid());
        clock[0] = NOW + Duration.ofMinutes(45).toMillis();
        assertTrue(replicaA.validate(s.sessionId(), "alice").isValid());   // 25 min after B's touch: still inside 30

        assertTrue(replicaB.terminate(s.sessionId(), "alice"));
        assertEquals(Status.NOT_FOUND, replicaA.validate(s.sessionId(), "alice").status());
        assertFalse(replicaA.isLive(s.sessionId()));
        assertFalse(replicaA.terminate(s.sessionId(), "alice"));
    }

    @Test
    void idleExpiryAndPurgeAreSharedAcrossReplicas() {
        var r = server();
        String prefix = "{t" + System.nanoTime() + "}:sessions:";
        long[] clock = {NOW};
        var options = new Options(Duration.ofMinutes(1), Duration.ofHours(1), 100);
        var a = new FoundgineMcpSessionManager(options, new RedisMcpSessionStore(r, prefix), () -> clock[0]);
        var b = new FoundgineMcpSessionManager(options, new RedisMcpSessionStore(r, prefix), () -> clock[0]);
        var s1 = a.create(null);
        var s2 = a.create(null);
        assertEquals(2, b.activeSessionCount());
        clock[0] = NOW + Duration.ofMinutes(2).toMillis();
        assertEquals(Status.EXPIRED, b.validate(s1.sessionId(), null).status());
        assertEquals(1, b.purgeExpired());   // s2, which only A knew about
        assertEquals(0, a.activeSessionCount());
        assertEquals(Status.NOT_FOUND, a.validate(s2.sessionId(), null).status());
    }

    @Test
    void sessionCapAppliesToTheSharedCount() {
        var r = server();
        String prefix = "{t" + System.nanoTime() + "}:sessions:";
        var options = new Options(Duration.ofMinutes(1), Duration.ofHours(1), 2);
        var a = new FoundgineMcpSessionManager(options, new RedisMcpSessionStore(r, prefix), () -> NOW);
        var b = new FoundgineMcpSessionManager(options, new RedisMcpSessionStore(r, prefix), () -> NOW);
        a.create(null);
        b.create(null);
        assertThrows(IllegalStateException.class, () -> a.create(null));
    }

    @Test
    void keysAreScopedByPrefix() {
        var r = server();
        long id = System.nanoTime();
        var one = new RedisMcpSessionStore(r, "{p" + id + "a}:sessions:");
        var two = new RedisMcpSessionStore(r, "{p" + id + "b}:sessions:");
        one.tryAdd(session("k", null, NOW + 1, NOW + 2));
        assertEquals(null, two.find("k"));
        assertEquals(0, two.count());
        assertTrue(two.tryAdd(session("k", null, NOW + 1, NOW + 2)));
    }
}
