package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.Options;
import java.time.Duration;
import java.util.concurrent.atomic.AtomicLong;
import org.junit.jupiter.api.Test;

class McpSessionGateTest {
    private final AtomicLong now = new AtomicLong(1_000_000L);

    private FoundgineMcpSessionGate gate(int max) {
        return new FoundgineMcpSessionGate(new FoundgineMcpSessionManager(
                new Options(Duration.ofMinutes(10), Duration.ofHours(1), max),
                new FoundgineMcpSessionManager.InMemoryStore(), now::get));
    }

    @Test void initializeIssuesASessionAndLaterRequestsNeedIt() {
        var g = gate(10);
        var init = g.admitPost("initialize", null, "alice");
        assertTrue(init.proceeds());
        assertNotNull(init.issuedSessionId());
        var call = g.admitPost("tools/call", init.issuedSessionId(), "alice");
        assertTrue(call.proceeds());
        assertNull(call.issuedSessionId());
    }

    @Test void missingHeaderIs400AndUnknownMalformedOrForeignIs404() {
        var g = gate(10);
        var id = g.admitPost("initialize", null, "alice").issuedSessionId();
        assertEquals(400, g.admitPost("tools/list", null, "alice").httpStatus());
        assertEquals(400, g.admitPost("tools/list", "  ", "alice").httpStatus());
        assertEquals(404, g.admitPost("tools/list", "nope", "alice").httpStatus());
        assertEquals(404, g.admitPost("tools/list", "a".repeat(43), "alice").httpStatus());
        assertEquals(404, g.admitPost("tools/list", id, "mallory").httpStatus());
        assertEquals(400, g.admitPost(null, null, "alice").httpStatus());
        assertTrue(g.admitPost(null, id, "alice").proceeds());   // unreadable body: the server reports the parse error
    }

    @Test void initializeIgnoresAnyHeaderAndNeverReusesASession() {
        var g = gate(10);
        var first = g.admitPost("initialize", null, "alice").issuedSessionId();
        var second = g.admitPost("initialize", first, "alice");
        assertNotNull(second.issuedSessionId());
        assertFalse(first.equals(second.issuedSessionId()));
    }

    @Test void deleteTerminatesOnceAndOnlyForTheOwner() {
        var g = gate(10);
        var id = g.admitPost("initialize", null, "alice").issuedSessionId();
        assertEquals(404, g.admitDelete(id, "mallory").httpStatus());
        assertEquals(400, g.admitDelete(null, "alice").httpStatus());
        assertEquals(204, g.admitDelete(id, "alice").httpStatus());
        assertEquals(404, g.admitDelete(id, "alice").httpStatus());
        assertEquals(404, g.admitPost("tools/list", id, "alice").httpStatus());
    }

    @Test void expiredSessionsLookLikeUnknownOnes() {
        var g = gate(10);
        var id = g.admitPost("initialize", null, null).issuedSessionId();
        now.addAndGet(Duration.ofMinutes(10).toMillis());
        assertEquals(404, g.admitPost("tools/list", id, null).httpStatus());
    }

    @Test void fullSessionTableAnswers503() {
        var g = gate(1);
        assertTrue(g.admitPost("initialize", null, null).proceeds());
        var full = g.admitPost("initialize", null, null);
        assertFalse(full.proceeds());
        assertEquals(503, full.httpStatus());
    }

    @Test void getIsAdmittedOnlyForALiveOwnedSession() {
        var g = gate(10);
        String id = g.admitPost("initialize", null, "alice").issuedSessionId();
        assertTrue(g.admitGet(id, "alice").proceeds());
        assertEquals(400, g.admitGet(null, "alice").httpStatus());
        assertEquals(400, g.admitGet("  ", "alice").httpStatus());
        assertEquals(404, g.admitGet(id, "mallory").httpStatus());
        assertEquals(404, g.admitGet("nope", "alice").httpStatus());
        g.admitDelete(id, "alice");
        assertEquals(404, g.admitGet(id, "alice").httpStatus());
    }
}
