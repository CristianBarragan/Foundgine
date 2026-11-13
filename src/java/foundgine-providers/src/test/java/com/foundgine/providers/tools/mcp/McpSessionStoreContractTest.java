package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.StoredSession;
import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.Store;
import java.time.Duration;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.Supplier;
import org.junit.jupiter.api.Test;

/**
 * The contract every {@link Store} must meet, runnable against any implementation via {@link #verify}. A clustered host
 * implementing a shared store (Redis, SQL, ...) copies this method into its own test suite. The second case below is a store
 * that, like a remote one, hands out copies and shares nothing by reference.
 */
class McpSessionStoreContractTest {
    static void verify(Supplier<Store> factory) {
        Store store = factory.get();
        var s = new StoredSession("k1", null, 100, 100, 500, 900);
        assertTrue(store.tryAdd(s));
        assertFalse(store.tryAdd(new StoredSession("k1", "other", 1, 1, 2, 3)), "a colliding key must not overwrite");
        assertEquals(s, store.find("k1"));
        assertNull(store.find("missing"));
        assertEquals(1, store.count());

        var touched = store.touch("k1", 300, 700);
        assertEquals(300, touched.lastAccessedAtUnixMilliseconds());
        assertEquals(700, touched.idleExpiresAtUnixMilliseconds());
        assertEquals(900, touched.absoluteExpiresAtUnixMilliseconds());
        assertEquals(100, touched.createdAtUnixMilliseconds());
        var backwards = store.touch("k1", 50, 400);   // never moves deadlines or access time backwards
        assertEquals(300, backwards.lastAccessedAtUnixMilliseconds());
        assertEquals(700, backwards.idleExpiresAtUnixMilliseconds());
        assertNull(store.touch("missing", 1, 2));
        assertNull(store.find("missing"), "touch must never create a session");

        store.tryAdd(new StoredSession("k2", null, 100, 100, 200, 900));
        store.tryAdd(new StoredSession("k3", null, 100, 100, 5_000, 800));
        assertEquals(1, store.removeExpired(200), "earlier of idle/absolute deadline, inclusive");
        assertEquals(0, store.removeExpired(200));
        assertEquals(2, store.removeExpired(800));
        assertEquals(0, store.count());
        assertFalse(store.remove("k1"));
        store.tryAdd(s);
        assertTrue(store.remove("k1"));
        assertFalse(store.remove("k1"));
    }

    @Test void inMemoryStoreMeetsTheContract() { verify(FoundgineMcpSessionManager.InMemoryStore::new); }

    /** Copies on the way in and out, as a remote store would. */
    static final class CopyingStore implements Store {
        private final Map<String, StoredSession> data = new ConcurrentHashMap<>();
        private static StoredSession copy(StoredSession s) {
            return s == null ? null : new StoredSession(s.key(), s.ownerDigest(), s.createdAtUnixMilliseconds(),
                    s.lastAccessedAtUnixMilliseconds(), s.idleExpiresAtUnixMilliseconds(), s.absoluteExpiresAtUnixMilliseconds());
        }
        @Override public boolean tryAdd(StoredSession s) { return data.putIfAbsent(s.key(), copy(s)) == null; }
        @Override public StoredSession find(String key) { return copy(data.get(key)); }
        @Override public StoredSession touch(String key, long accessed, long idleExpires) {
            return copy(data.computeIfPresent(key, (k, s) -> new StoredSession(k, s.ownerDigest(), s.createdAtUnixMilliseconds(),
                    Math.max(accessed, s.lastAccessedAtUnixMilliseconds()), Math.max(idleExpires, s.idleExpiresAtUnixMilliseconds()),
                    s.absoluteExpiresAtUnixMilliseconds())));
        }
        @Override public boolean remove(String key) { return data.remove(key) != null; }
        @Override public int removeExpired(long now) {
            int[] n = {0};
            data.values().removeIf(s -> { boolean dead = s.expiresAt() <= now; if (dead) n[0]++; return dead; });
            return n[0];
        }
        @Override public int count() { return data.size(); }
    }

    @Test void aCopyingStoreMeetsTheContractAndDrivesTheManager() {
        verify(CopyingStore::new);
        var now = new AtomicLong(1_000);
        var m = new FoundgineMcpSessionManager(new FoundgineMcpSessionManager.Options(Duration.ofMillis(100), Duration.ofMillis(300), 5),
                new CopyingStore(), now::get);
        var s = m.create("alice");
        assertTrue(m.validate(s.sessionId(), "alice").isValid());
        assertFalse(m.validate(s.sessionId(), "bob").isValid());
        now.addAndGet(99);   // just inside the 100 ms idle window
        assertTrue(m.validate(s.sessionId(), "alice").isValid());
        assertTrue(m.terminate(s.sessionId(), "alice"));
        assertFalse(m.validate(s.sessionId(), "alice").isValid());
    }
}
