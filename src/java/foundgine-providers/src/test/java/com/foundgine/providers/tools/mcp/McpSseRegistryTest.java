package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.Options;
import java.time.Duration;
import java.util.concurrent.atomic.AtomicLong;
import org.junit.jupiter.api.Test;

class McpSseRegistryTest {
    private static final String A = "A".repeat(43), B = "B".repeat(43), C = "C".repeat(43);

    @Test void oneBufferPerSessionAndLeastRecentlyUsedIsDropped() {
        var reg = new FoundgineMcpSseRegistry(2, 5, 100);
        var a = reg.stream(A);
        assertSame(a, reg.stream(A));
        var b = reg.stream(B);
        assertNotSame(a, b);
        reg.stream(A);          // A is now most recent, B least
        reg.stream(C);          // evicts B
        assertEquals(2, reg.count());
        assertSame(a, reg.stream(A));
        assertNotSame(b, reg.stream(B));   // B was recreated empty
    }

    @Test void removeAndIllFormedIds() {
        var reg = new FoundgineMcpSseRegistry();
        reg.stream(A);
        assertTrue(reg.remove(A));
        assertFalse(reg.remove(A));
        assertThrows(IllegalArgumentException.class, () -> reg.stream("short"));
        assertThrows(IllegalArgumentException.class, () -> reg.stream(null));
        assertThrows(IllegalArgumentException.class, () -> new FoundgineMcpSseRegistry(0, 1, 1));
        assertThrows(IllegalArgumentException.class, () -> new FoundgineMcpSseRegistry(1, 0, 1));
    }

    @Test void leavingTheRegistryRetiresTheBuffer() {
        var reg = new FoundgineMcpSseRegistry(1, 5, 100);
        var a = reg.stream(A);
        reg.stream(B);                       // evicts A
        assertTrue(a.isRetired());
        var b = reg.find(B);
        assertFalse(b.isRetired());
        assertTrue(reg.remove(B));
        assertTrue(b.isRetired());
    }

    @Test void sweepDropsOnlyBuffersOfSessionsThatAreNotLive() {
        var reg = new FoundgineMcpSseRegistry();
        var a = reg.stream(A);
        var b = reg.stream(B);
        var told = new java.util.ArrayList<String>();
        a.subscribe(() -> { }, () -> told.add("a"));
        assertEquals(1, reg.sweep(id -> id.equals(B)));
        assertNull(reg.find(A));
        assertSame(b, reg.find(B));
        assertEquals(java.util.List.of("a"), told);
        assertEquals(0, reg.sweep(id -> true));
    }

    @Test void isLiveDoesNotSlideTheDeadlineOrCheckTheOwner() {
        var now = new AtomicLong(1_000_000L);
        var manager = new FoundgineMcpSessionManager(
                new Options(Duration.ofMinutes(10), Duration.ofHours(1), 10), new FoundgineMcpSessionManager.InMemoryStore(), now::get);
        var id = manager.create("alice").sessionId();
        assertTrue(manager.isLive(id));
        assertFalse(manager.isLive("short"));
        assertFalse(manager.isLive(A));
        now.addAndGet(9 * 60_000L);
        assertTrue(manager.isLive(id));        // still inside the idle window ...
        now.addAndGet(2 * 60_000L);
        assertFalse(manager.isLive(id));       // ... and the check above did not extend it
    }
}
