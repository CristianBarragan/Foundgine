package com.foundgine.providers.security.redis;

import static org.junit.jupiter.api.Assertions.*;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;

/** Exercises the store's logic against a fake backend with SET NX PX semantics; no server is needed. */
class RedisSecurityWarrantReplayStoreTest {
    private static final Instant NOW = Instant.parse("2026-01-01T00:00:00Z");
    private static final Clock CLOCK = Clock.fixed(NOW, ZoneOffset.UTC);

    private static final class FakeBackend implements ReplayBackend {
        final Map<String, Duration> keys = new ConcurrentHashMap<>();
        final List<Duration> ttls = Collections.synchronizedList(new ArrayList<>());

        @Override
        public boolean setIfAbsent(String key, Duration ttl) {
            ttls.add(ttl);
            return keys.putIfAbsent(key, ttl) == null;
        }
    }

    private static RedisSecurityWarrantReplayStore store(ReplayBackend backend) {
        return new RedisSecurityWarrantReplayStore(
                backend,
                RedisSecurityWarrantReplayStore.DEFAULT_KEY_PREFIX,
                Duration.ofHours(1),
                Duration.ofMinutes(1),
                CLOCK);
    }

    @Test
    void firstConsumeWinsAndEverySecondIsRejected() {
        var s = store(new FakeBackend());
        assertTrue(s.tryConsume("w", "n", NOW.plusSeconds(300)));
        assertFalse(s.tryConsume("w", "n", NOW.plusSeconds(300)));
        assertTrue(s.tryConsume("w", "other-nonce", NOW.plusSeconds(300)));
        assertTrue(s.tryConsume("other-warrant", "n", NOW.plusSeconds(300)));
    }

    @Test
    void ttlIsRemainingLifetimePlusSkewGrace() {
        var backend = new FakeBackend();
        store(backend).tryConsume("w", "n", NOW.plusSeconds(300));
        assertEquals(Duration.ofSeconds(360), backend.ttls.get(0));
    }

    @Test
    void alreadyExpiredWarrantStillGetsTheShortGraceTtlNeverZeroOrNegative() {
        var backend = new FakeBackend();
        store(backend).tryConsume("w", "n", NOW.minusSeconds(30));
        assertEquals(Duration.ofMinutes(1), backend.ttls.get(0));
    }

    @Test
    void twoArgumentOverloadUsesTheDefaultRetention() {
        var backend = new FakeBackend();
        assertTrue(store(backend).tryConsume("w", "n"));
        assertEquals(Duration.ofHours(1), backend.ttls.get(0));
    }

    @Test
    void absurdExpiryIsCappedSoTheServerDoesNotRejectIt() {
        var backend = new FakeBackend();
        store(backend).tryConsume("w", "n", Instant.MAX);
        assertEquals(Duration.ofDays(3650), backend.ttls.get(0));
    }

    @Test
    void keysAreHashedWithThePrefixAndNeverContainTheRawNonce() {
        var backend = new FakeBackend();
        store(backend).tryConsume("warrant-id", "secret-nonce-value", NOW.plusSeconds(300));
        String key = backend.keys.keySet().iterator().next();
        assertTrue(key.startsWith("foundgine:warrant-replay:"));
        assertFalse(key.contains("secret-nonce-value") || key.contains("warrant-id"));
        assertEquals(RedisSecurityWarrantReplayStore.DEFAULT_KEY_PREFIX.length() + 64, key.length());
        assertTrue(key.substring(RedisSecurityWarrantReplayStore.DEFAULT_KEY_PREFIX.length()).matches("[0-9A-F]{64}"));
    }

    @Test
    void sameIdentityAsTheFileStoreSoKeysAreStableAcrossLanguages() {
        var backend = new FakeBackend();
        store(backend).tryConsume("w-1", "n-1", NOW.plusSeconds(300));
        assertEquals(
                "foundgine:warrant-replay:4F315D677F0C686D00E3CABD5847218BA0F8413BE77708988FB7197AE5618C7E",
                backend.keys.keySet().iterator().next());
    }

    @Test
    void backendFailureFailsClosedAndNeverReturnsTrue() {
        ReplayBackend down = (key, ttl) -> {
            throw new IllegalStateException("connection refused");
        };
        var s = store(down);
        var ex = assertThrows(IllegalStateException.class, () -> s.tryConsume("w", "n", NOW.plusSeconds(300)));
        assertTrue(ex.getMessage().contains("failing closed"));
        assertThrows(IllegalStateException.class, () -> s.tryConsume("w", "n"));
    }

    @Test
    void blankIdentityIsRejectedBeforeTouchingTheBackend() {
        var backend = new FakeBackend();
        var s = store(backend);
        assertThrows(IllegalArgumentException.class, () -> s.tryConsume(" ", "n", NOW));
        assertThrows(IllegalArgumentException.class, () -> s.tryConsume("w", null, NOW));
        assertEquals(0, backend.ttls.size());
    }

    @Test
    void exactlyOneOfManyConcurrentConsumersWins() throws Exception {
        var s = store(new FakeBackend());
        int threads = 64;
        var pool = Executors.newFixedThreadPool(threads);
        var start = new CountDownLatch(1);
        var winners = new AtomicInteger();
        for (int i = 0; i < threads; i++)
            pool.submit(() -> {
                start.await();
                if (s.tryConsume("race", "n", NOW.plusSeconds(300))) winners.incrementAndGet();
                return null;
            });
        start.countDown();
        pool.shutdown();
        assertTrue(pool.awaitTermination(10, TimeUnit.SECONDS));
        assertEquals(1, winners.get());
    }
}
