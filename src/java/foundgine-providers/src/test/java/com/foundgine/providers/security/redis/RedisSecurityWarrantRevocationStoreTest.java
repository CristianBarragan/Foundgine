package com.foundgine.providers.security.redis;

import static org.junit.jupiter.api.Assertions.*;

import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.CompletableFuture;
import org.junit.jupiter.api.Test;

class RedisSecurityWarrantRevocationStoreTest {
    @Test void validatesSingleValueSequenceResponse() {
        var store = new RedisSecurityWarrantRevocationStore((script, keys, args) -> List.of("23"));
        assertEquals(23, store.currentSequence());
    }

    @Test void rejectsMalformedSequenceResponse() {
        var store = new RedisSecurityWarrantRevocationStore((script, keys, args) -> List.of("23", "extra"));
        assertThrows(IllegalStateException.class, store::currentSequence);
    }

    @Test void failsClosedWhenRedisIsUnavailable() {
        var store = new RedisSecurityWarrantRevocationStore((script, keys, args) -> { throw new IllegalStateException("offline"); });
        assertThrows(IllegalStateException.class, store::currentSequence);
    }

    @Test void outOfOrderCompletionOfConcurrentCallsIsNotARollback() throws Exception {
        var calls = new AtomicInteger();
        var entered = new CountDownLatch(1);
        var release = new CountDownLatch(1);
        long[] next = {48};
        var store = new RedisSecurityWarrantRevocationStore((script, keys, args) -> {
            if (calls.incrementAndGet() == 1) {
                entered.countDown();
                try { assertTrue(release.await(10, TimeUnit.SECONDS)); } catch (InterruptedException e) { throw new IllegalStateException(e); }
                return List.of("47");
            }
            return List.of(Long.toString(next[0]));
        });
        // Call A reaches Redis first (47) but is delayed; call B reaches Redis second (48) and returns first.
        var a = CompletableFuture.supplyAsync(store::currentSequence);
        assertTrue(entered.await(10, TimeUnit.SECONDS));
        assertEquals(48, store.currentSequence());
        release.countDown();
        assertEquals(47, a.get(10, TimeUnit.SECONDS));
        // The high-water mark stays at 48, so a genuine later rollback is still detected.
        next[0] = 47;
        assertThrows(IllegalStateException.class, store::currentSequence);
    }

    @Test void lookupReturnsOneAtomicSnapshot() {
        var seen = new java.util.ArrayList<List<String>>();
        var store = new RedisSecurityWarrantRevocationStore((script, keys, args) -> { seen.add(keys); return List.of("9", "0", "1"); });
        var lookup = store.lookup("id", "digest", List.of("p1", "p2"));
        assertEquals(9, lookup.sequence());
        assertFalse(lookup.revoked());
        assertTrue(lookup.ancestorRevoked());
        assertEquals(1, seen.size());
        assertEquals(5, seen.get(0).size()); // sequence, identity, digest, two ancestors
    }

    @Test void lookupRejectsMalformedReplyAndRollback() {
        var bad = new RedisSecurityWarrantRevocationStore((script, keys, args) -> List.of("9", "2", "0"));
        assertThrows(IllegalStateException.class, () -> bad.lookup("id", "d", List.of()));
        long[] seq = {10};
        var store = new RedisSecurityWarrantRevocationStore((script, keys, args) -> List.of(Long.toString(seq[0]), "0", "0"));
        assertEquals(10, store.lookup("id", "d", List.of()).sequence());
        seq[0] = 9;
        assertThrows(IllegalStateException.class, () -> store.lookup("id", "d", List.of()));
    }

    @Test void lookupFailsClosedWhenRedisIsUnavailable() {
        var store = new RedisSecurityWarrantRevocationStore((script, keys, args) -> { throw new IllegalStateException("offline"); });
        assertThrows(IllegalStateException.class, () -> store.lookup("id", "d", List.of()));
    }
}
