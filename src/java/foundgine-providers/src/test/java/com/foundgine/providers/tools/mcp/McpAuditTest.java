package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.providers.tools.mcp.FoundgineMcpAuditEvent.Type;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicLong;
import org.junit.jupiter.api.Test;

class McpAuditTest {
    private final AtomicLong now = new AtomicLong(5_000_000L);
    private final List<FoundgineMcpAuditEvent> events = new ArrayList<>();

    private static KeyPair pair() throws Exception {
        var g = KeyPairGenerator.getInstance("RSA");
        g.initialize(2048);
        return g.generateKeyPair();
    }

    private List<Type> types() { return events.stream().map(FoundgineMcpAuditEvent::type).toList(); }

    @Test void keyLifecycleIsAuditedWithoutKeyMaterial() throws Exception {
        var ring = new FoundgineMcpApprovalKeyRing(Duration.ofMinutes(5), now::get, events::add);
        var a = pair(); var b = pair(); var c = pair();
        ring.activateSigningKey("k1", a.getPrivate(), a.getPublic());
        ring.activateSigningKey("k2", b.getPrivate(), b.getPublic(), Duration.ofMinutes(10));
        ring.addVerificationKey("ext", c.getPublic(), java.time.Instant.ofEpochMilli(now.get() + Duration.ofMinutes(20).toMillis()));
        assertTrue(ring.retireAfter("ext", Duration.ofMinutes(6)));
        assertTrue(ring.revoke("k1"));
        assertEquals(List.of(Type.KEY_ACTIVATED, Type.KEY_ROTATED, Type.KEY_ADDED, Type.KEY_RETIRED, Type.KEY_REVOKED), types());
        assertEquals("k2", events.get(1).subject());
        assertTrue(events.get(1).detail().contains("previous=k1"));
        assertEquals(now.get(), events.get(0).atUnixMilliseconds());
        assertTrue(events.get(4).detail().contains("removed=true"));
        for (var e : events) assertFalse(e.toString().contains("MII"), "no key material in audit events");
    }

    @Test void failedOperationsEmitNothingAndABrokenSinkNeverBreaksThem() throws Exception {
        var ring = new FoundgineMcpApprovalKeyRing(Duration.ofMinutes(5), now::get, events::add);
        var a = pair();
        ring.activateSigningKey("k1", a.getPrivate(), a.getPublic());
        events.clear();
        assertThrows(IllegalArgumentException.class, () -> ring.activateSigningKey("k1", a.getPrivate(), a.getPublic(), Duration.ofMinutes(10)));
        assertThrows(IllegalArgumentException.class, () -> ring.addVerificationKey("x", a.getPublic(), java.time.Instant.ofEpochMilli(now.get() + 1000)));
        assertEquals(List.of(), events);

        var broken = new FoundgineMcpApprovalKeyRing(Duration.ZERO, now::get, e -> { throw new IllegalStateException("sink down"); });
        broken.activateSigningKey("k1", a.getPrivate(), a.getPublic());
        assertTrue(broken.revoke("k1"));
        assertThrows(IllegalStateException.class, broken::keyId);   // the revocation took effect despite the sink failing
    }

    @Test void sessionEventsCarryFingerprintsNotIdsAndNothingForGarbage() {
        var manager = new FoundgineMcpSessionManager(new FoundgineMcpSessionManager.Options(Duration.ofMinutes(10), Duration.ofMinutes(25), 1),
                new FoundgineMcpSessionManager.InMemoryStore(), now::get);
        var gate = new FoundgineMcpSessionGate(manager, events::add, now::get);

        var init = gate.admitPost("initialize", null, "alice");
        String id = init.issuedSessionId();
        assertEquals(Type.SESSION_ISSUED, events.get(0).type());
        assertEquals(FoundgineMcpSessionManager.fingerprint(id), events.get(0).subject());
        assertEquals("alice", events.get(0).actor());

        assertEquals(503, gate.admitPost("initialize", null, "alice").httpStatus());
        gate.admitPost("ping", null, "alice");
        gate.admitPost("ping", id, "mallory");
        gate.admitPost("ping", "<script>alert(1)</script>", "alice");
        gate.admitDelete(id, "mallory");
        gate.admitDelete(id, "alice");
        gate.admitDelete(id, "alice");
        assertEquals(List.of(Type.SESSION_ISSUED, Type.SESSION_TABLE_FULL, Type.SESSION_REJECTED, Type.SESSION_REJECTED,
                Type.SESSION_REJECTED, Type.SESSION_REJECTED, Type.SESSION_TERMINATED, Type.SESSION_REJECTED), types());
        assertTrue(events.get(2).detail().contains("MISSING"));
        assertTrue(events.get(3).detail().contains("OWNER_MISMATCH"));
        assertNull(events.get(4).subject());   // malformed id: attacker text is never logged
        for (var e : events) assertFalse(e.toString().contains(id));
        assertEquals(12, events.get(0).subject().length());
    }
}
