package com.foundgine.runtime.controlplane;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.runtime.controlplane.auditlog.AuditEvent;
import com.foundgine.runtime.controlplane.auditlog.BufferedDurableAuditLog;
import java.io.IOException;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

class BufferedDurableAuditLogTest {
    private static AuditEvent event(String name) {
        return new AuditEvent(AuditEvent.AuditCategory.ROUTED, name, "actor", "tenant", "fp", "summary", Instant.now());
    }

    @Test
    void recordReturnsAfterDurableSinkAcknowledgementAndQueryShowsCommittedEvents() {
        var persisted = new ArrayList<AuditEvent>();
        try (var log = new BufferedDurableAuditLog(persisted::add, 1)) {
            var item = event("tool-a");
            log.record(item);
            assertEquals(List.of(item), persisted);
            assertEquals(List.of(item), log.query("tool-a", "tenant"));
            assertEquals(1, log.metrics().accepted());
            assertEquals(1, log.metrics().persisted());
            assertEquals(0, log.metrics().failed());
        }
    }

    @Test
    void sinkFailureIsPropagatedAndEventIsNotReportedCommitted() {
        try (var log = new BufferedDurableAuditLog(item -> { throw new IOException("sink unavailable"); }, 1)) {
            var failure = assertThrows(IllegalStateException.class, () -> log.record(event("tool-b")));
            assertTrue(failure.getMessage().contains("append failed"));
            assertTrue(log.query().isEmpty());
            assertEquals(1, log.metrics().failed());
            assertEquals(0, log.metrics().persisted());
        }
    }

    @Test
    void closeDrainsAcceptedEvents() {
        var persisted = new ArrayList<AuditEvent>();
        var log = new BufferedDurableAuditLog(persisted::add, 2);
        log.record(event("one"));
        log.record(event("two"));
        log.close();
        assertEquals(2, persisted.size());
        assertThrows(IllegalStateException.class, () -> log.record(event("three")));
    }
}
