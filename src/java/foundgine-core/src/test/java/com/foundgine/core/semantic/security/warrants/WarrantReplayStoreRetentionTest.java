package com.foundgine.core.semantic.security.warrants;

import static org.junit.jupiter.api.Assertions.*;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

/** Replay stores must reject a second consume yet stay bounded: entries are dropped once the warrant has expired. */
class WarrantReplayStoreRetentionTest {
    private static final Instant T0 = Instant.parse("2026-01-01T00:00:00Z");

    private static final class MutableClock extends Clock {
        private Instant now = T0;

        void advance(Duration d) {
            now = now.plus(d);
        }

        @Override public ZoneOffset getZone() { return ZoneOffset.UTC; }
        @Override public Clock withZone(java.time.ZoneId zone) { return this; }
        @Override public Instant instant() { return now; }
    }

    @Test
    void memoryStoreRejectsReplayWithAndWithoutExpiry() {
        var store = new MemorySecurityWarrantReplayStore();
        assertTrue(store.tryConsume("w", "n1"));
        assertFalse(store.tryConsume("w", "n1"));
        assertTrue(store.tryConsume("w", "n2", T0.plusSeconds(300)));
        assertFalse(store.tryConsume("w", "n2", T0.plusSeconds(300)));
        assertFalse(store.tryConsume("w", "n2"), "the same pair is consumed regardless of overload");
    }

    @Test
    void memoryStoreDropsExpiredEntriesButKeepsLiveOnes() {
        var clock = new MutableClock();
        var store = new MemorySecurityWarrantReplayStore(clock);
        for (int i = 0; i < 400; i++) assertTrue(store.tryConsume("w" + i, "n", T0.plusSeconds(300)));
        assertTrue(store.tryConsume("long-lived", "n", T0.plusSeconds(3600)));
        assertTrue(store.tryConsume("no-expiry", "n"));

        clock.advance(Duration.ofSeconds(300).plus(MemorySecurityWarrantReplayStore.RETENTION_GRACE).plusSeconds(1));
        for (int i = 0; i < 300; i++) assertTrue(store.tryConsume("fresh" + i, "n", T0.plus(Duration.ofHours(2))));

        assertTrue(store.size() < 400, "expired entries must be evicted, size=" + store.size());
        assertFalse(store.tryConsume("long-lived", "n", T0.plusSeconds(3600)), "live entry survives the purge");
        assertFalse(store.tryConsume("no-expiry", "n"), "entry without a lifetime is never evicted");
    }

    @Test
    void entryIsRetainedThroughSkewGraceAfterExpiry() {
        var clock = new MutableClock();
        var store = new MemorySecurityWarrantReplayStore(clock);
        assertTrue(store.tryConsume("w", "n", T0.plusSeconds(60)));
        clock.advance(Duration.ofSeconds(61)); // expired, but inside the grace window
        for (int i = 0; i < 300; i++) store.tryConsume("x" + i, "n", T0.plus(Duration.ofHours(1)));
        assertFalse(store.tryConsume("w", "n", T0.plusSeconds(60)));
    }

    @Test
    void guardPassesTheWarrantExpiryToTheStoreAndStillRejectsReplay() {
        Instant now = Instant.now();
        Instant expires = now.plusSeconds(300);
        var warrant = SecurityWarrant.ofDefaults(
                "w-retention", "issuer", "agent", "audience", List.of(), new SecurityWarrantConstraints(),
                now.minusSeconds(5), expires, "nonce", "key", null, new byte[0]);
        var seen = new java.util.ArrayList<Instant>();
        var inner = new MemorySecurityWarrantReplayStore();
        ISecurityWarrantReplayStore recording = new ISecurityWarrantReplayStore() {
            @Override public boolean tryConsume(String id, String nonce) { return inner.tryConsume(id, nonce); }
            @Override public boolean tryConsume(String id, String nonce, Instant expiresAt) {
                seen.add(expiresAt);
                return inner.tryConsume(id, nonce, expiresAt);
            }
        };
        SecurityWarrantReplayGuard.consume(warrant, recording, now);
        assertEquals(List.of(warrant.expiresAt()), seen);
        assertThrows(IllegalStateException.class, () -> SecurityWarrantReplayGuard.consume(warrant, recording, now));
    }

    @Test
    void legacyCustomStoreThatOnlyImplementsTwoArgumentFormStillWorks() {
        var consumed = new java.util.HashSet<String>();
        ISecurityWarrantReplayStore legacy = (id, nonce) -> consumed.add(id + nonce);
        assertTrue(legacy.tryConsume("w", "n", T0));
        assertFalse(legacy.tryConsume("w", "n", T0));
    }

    @Test
    void fileStoreRejectsReplayAcrossInstances(@TempDir Path dir) {
        Path file = dir.resolve("replay.log");
        var a = new FileSecurityWarrantReplayStore(file.toString());
        var b = new FileSecurityWarrantReplayStore(file.toString());
        Instant farFuture = Instant.now().plus(Duration.ofDays(1));
        assertTrue(a.tryConsume("w", "n", farFuture));
        assertFalse(b.tryConsume("w", "n", farFuture));
    }

    @Test
    void fileStoreKeepsLegacyLinesWithoutALifetime(@TempDir Path dir) throws IOException {
        Path file = dir.resolve("replay.log");
        var store = new FileSecurityWarrantReplayStore(file.toString());
        assertTrue(store.tryConsume("old", "n")); // legacy form: bare hash line
        assertFalse(store.tryConsume("old", "n", T0.plusSeconds(60)));
        assertFalse(Files.readString(file).contains("\t"));
    }

    @Test
    void fileStoreCompactsExpiredEntriesAndKeepsLiveOnes(@TempDir Path dir) throws IOException {
        Path file = dir.resolve("replay.log");
        var clock = new MutableClock();
        var store = new FileSecurityWarrantReplayStore(file.toString(), clock);
        for (int i = 0; i < 20; i++) assertTrue(store.tryConsume("short" + i, "n", T0.plusSeconds(60)));
        assertTrue(store.tryConsume("live", "n", T0.plus(Duration.ofHours(1))));
        assertEquals(21, lines(file));

        clock.advance(Duration.ofSeconds(60).plus(MemorySecurityWarrantReplayStore.RETENTION_GRACE).plusSeconds(1));
        assertTrue(store.tryConsume("next", "n", T0.plus(Duration.ofHours(2))));

        assertEquals(2, lines(file), "expired lines are dropped, live and new lines remain");
        assertFalse(store.tryConsume("live", "n", T0.plus(Duration.ofHours(1))));
        assertFalse(store.tryConsume("next", "n", T0.plus(Duration.ofHours(2))));
        try (var tmp = Files.list(dir)) {
            assertEquals(0, tmp.filter(p -> p.getFileName().toString().endsWith(".tmp")).count(), "no temp file left behind");
        }
    }

    private static long lines(Path file) throws IOException {
        return Files.readAllLines(file, StandardCharsets.UTF_8).stream().filter(l -> !l.isEmpty()).count();
    }
}
