package com.foundgine.providers.security.redis;

import static org.junit.jupiter.api.Assertions.*;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

import com.foundgine.providers.tools.mcp.redis.RespRedis;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;

/** The replay store against a real Redis-protocol server; skipped unless {@code FOUNDGINE_REDIS=host:port} is set. */
class RedisReplayStoreServerTest {
    private static RespRedis server() {
        RespRedis r = RespRedis.fromEnvironment();
        assumeTrue(r != null, "FOUNDGINE_REDIS is not set");
        return r;
    }

    private static String prefix() {
        return "test:replay:" + System.nanoTime() + ":";
    }

    @Test
    void firstConsumeWinsAcrossStoreInstancesSharingAServer() {
        var r = server();
        String p = prefix();
        var a = new RedisSecurityWarrantReplayStore(r, p, Duration.ofHours(1), Duration.ofMinutes(1), Clock.systemUTC());
        var b = new RedisSecurityWarrantReplayStore(r, p, Duration.ofHours(1), Duration.ofMinutes(1), Clock.systemUTC());
        Instant exp = Instant.now().plusSeconds(300);
        assertTrue(a.tryConsume("w", "n", exp));
        assertFalse(b.tryConsume("w", "n", exp));
        assertFalse(a.tryConsume("w", "n", exp));
        assertTrue(b.tryConsume("w", "n2", exp));
    }

    @Test
    void ttlIsSetOnTheServerAndTheEntryDisappearsAfterIt() throws Exception {
        var r = server();
        String p = prefix();
        var s = new RedisSecurityWarrantReplayStore(r, p, Duration.ofHours(1), Duration.ofMillis(100), Clock.systemUTC());
        assertTrue(s.tryConsume("w", "n", Instant.now().plusMillis(150)));
        assertFalse(s.tryConsume("w", "n", Instant.now().plusMillis(150)));
        Thread.sleep(600);
        assertTrue(s.tryConsume("w", "n", Instant.now().plusMillis(150)), "entry should have expired on the server");

        var long1 = new RedisSecurityWarrantReplayStore(r, p + "long:", Duration.ofHours(1), Duration.ofMinutes(1), Clock.systemUTC());
        long1.tryConsume("w", "n", Instant.now().plusSeconds(300));
        Object key = ((java.util.List<?>) r.command("KEYS", p + "long:*")).get(0);
        long pttl = (Long) r.command("PTTL", key);
        assertTrue(pttl > 300_000 && pttl <= 360_000, "pttl=" + pttl);
    }

    @Test
    void exactlyOneOfManyConcurrentConsumersWinsOnARealServer() throws Exception {
        var r = server();
        var s = new RedisSecurityWarrantReplayStore(r, prefix(), Duration.ofHours(1), Duration.ofMinutes(1), Clock.systemUTC());
        int threads = 32;
        var pool = Executors.newFixedThreadPool(threads);
        var start = new CountDownLatch(1);
        var winners = new AtomicInteger();
        Instant exp = Instant.now().plusSeconds(300);
        for (int i = 0; i < threads; i++)
            pool.submit(() -> {
                start.await();
                if (s.tryConsume("race", "n", exp)) winners.incrementAndGet();
                return null;
            });
        start.countDown();
        pool.shutdown();
        assertTrue(pool.awaitTermination(20, TimeUnit.SECONDS));
        assertEquals(1, winners.get());
    }

    @Test
    void anUnreachableServerFailsClosed() {
        var dead = new RespRedis("127.0.0.1", 1);   // nothing listens on port 1
        var s = new RedisSecurityWarrantReplayStore(dead);
        assertThrows(IllegalStateException.class, () -> s.tryConsume("w", "n", Instant.now().plusSeconds(60)));
    }
}
