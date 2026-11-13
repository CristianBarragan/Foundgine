package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.Options;
import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.Status;
import java.time.Duration;
import java.util.HashSet;
import java.util.concurrent.atomic.AtomicLong;
import org.junit.jupiter.api.Test;

class McpSessionManagerTest {
    private final AtomicLong now = new AtomicLong(1_000_000L);

    private FoundgineMcpSessionManager manager(int max) {
        return new FoundgineMcpSessionManager(new Options(Duration.ofMinutes(10), Duration.ofMinutes(25), max),
                new FoundgineMcpSessionManager.InMemoryStore(), now::get);
    }

    @Test void issuesOpaqueUniqueUrlSafeIds() {
        var m = manager(1000);
        var ids = new HashSet<String>();
        for (int i = 0; i < 200; i++) {
            String id = m.create(null).sessionId();
            assertEquals(43, id.length());
            assertTrue(FoundgineMcpSessionManager.isWellFormed(id));
            assertTrue(ids.add(id));
            for (char c : id.toCharArray()) assertTrue(c >= 0x21 && c <= 0x7e);
        }
    }

    @Test void validatesAndSlidesIdleExpiryButNeverPastTheAbsoluteLimit() {
        var m = manager(10);
        var s = m.create(null);
        assertEquals(now.get() + Duration.ofMinutes(10).toMillis(), s.expiresAtUnixMilliseconds());
        now.addAndGet(Duration.ofMinutes(9).toMillis());
        var v = m.validate(s.sessionId(), null);
        assertTrue(v.isValid());
        assertEquals(now.get() + Duration.ofMinutes(10).toMillis(), v.session().expiresAtUnixMilliseconds());
        now.addAndGet(Duration.ofMinutes(9).toMillis());
        assertTrue(m.validate(s.sessionId(), null).isValid());
        now.addAndGet(Duration.ofMinutes(5).toMillis());   // 23 min since creation; absolute limit is 25
        var last = m.validate(s.sessionId(), null);
        assertTrue(last.isValid());
        assertEquals(s.createdAtUnixMilliseconds() + Duration.ofMinutes(25).toMillis(), last.session().expiresAtUnixMilliseconds());
        now.addAndGet(Duration.ofMinutes(2).toMillis());
        assertEquals(Status.EXPIRED, m.validate(s.sessionId(), null).status());
        assertEquals(Status.NOT_FOUND, m.validate(s.sessionId(), null).status());
    }

    @Test void idleSessionExpires() {
        var m = manager(10);
        var s = m.create(null);
        now.addAndGet(Duration.ofMinutes(10).toMillis());
        assertEquals(Status.EXPIRED, m.validate(s.sessionId(), null).status());
    }

    @Test void terminationIsExplicitIdempotentAndFinal() {
        var m = manager(10);
        var s = m.create(null);
        assertTrue(m.terminate(s.sessionId(), null));
        assertFalse(m.terminate(s.sessionId(), null));
        assertEquals(Status.NOT_FOUND, m.validate(s.sessionId(), null).status());
    }

    @Test void malformedUnknownAndNullIdsAreNotFound() {
        var m = manager(10);
        m.create(null);
        for (String bad : new String[] {null, "", "short", "x".repeat(43), "a".repeat(42) + "!", "a".repeat(44), "../".repeat(15)}) {
            assertEquals(Status.NOT_FOUND, m.validate(bad, null).status());
            assertFalse(m.terminate(bad, null));
        }
    }

    @Test void ownerBindingStopsAnotherPrincipalFromUsingOrEndingASession() {
        var m = manager(10);
        var s = m.create("alice");
        assertEquals(Status.OWNER_MISMATCH, m.validate(s.sessionId(), "mallory").status());
        assertEquals(Status.OWNER_MISMATCH, m.validate(s.sessionId(), null).status());
        assertFalse(m.terminate(s.sessionId(), "mallory"));
        assertFalse(m.terminate(s.sessionId(), null));
        assertTrue(m.validate(s.sessionId(), "alice").isValid());
        assertTrue(m.terminate(s.sessionId(), "alice"));
    }

    @Test void aSessionCarriesNoIdentityOrAuthority() {
        var m = manager(10);
        var s = m.create("alice");
        var fields = FoundgineMcpSessionManager.Session.class.getRecordComponents();
        for (var f : fields) assertFalse(f.getName().toLowerCase().contains("owner") || f.getName().toLowerCase().contains("subject"));
        assertEquals(4, fields.length);
        assertNotNull(s.sessionId());
    }

    @Test void enforcesSessionCapAndReclaimsExpiredSessions() {
        var m = manager(2);
        m.create(null);
        m.create(null);
        assertThrows(IllegalStateException.class, () -> m.create(null));
        now.addAndGet(Duration.ofMinutes(10).toMillis());
        assertNotNull(m.create(null));   // expired ones are reclaimed on demand
        assertEquals(1, m.activeSessionCount());
    }

    @Test void purgeRemovesOnlyExpired() {
        var m = manager(10);
        m.create(null);
        now.addAndGet(Duration.ofMinutes(6).toMillis());
        var keep = m.create(null);
        now.addAndGet(Duration.ofMinutes(5).toMillis());
        assertEquals(1, m.purgeExpired());
        assertTrue(m.validate(keep.sessionId(), null).isValid());
    }

    @Test void storeDoesNotHoldTheRawId() {
        var store = new FoundgineMcpSessionManager.InMemoryStore();
        var m = new FoundgineMcpSessionManager(Options.defaults(), store, now::get);
        var s = m.create(null);
        assertNull(store.find(s.sessionId()));
    }

    @Test void optionsAreValidated() {
        assertThrows(IllegalArgumentException.class, () -> new Options(Duration.ZERO, Duration.ofHours(1), 1));
        assertThrows(IllegalArgumentException.class, () -> new Options(Duration.ofHours(2), Duration.ofHours(1), 1));
        assertThrows(IllegalArgumentException.class, () -> new Options(Duration.ofHours(1), Duration.ofHours(1), 0));
    }
}
