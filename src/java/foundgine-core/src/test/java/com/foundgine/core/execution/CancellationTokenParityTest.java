package com.foundgine.core.execution;

import org.junit.jupiter.api.Test;

import java.util.concurrent.atomic.AtomicInteger;

import static org.junit.jupiter.api.Assertions.*;

/** Behavioral parity tests for live cancellation callback registration. */
class CancellationTokenParityTest {
    @Test
    void registration_runsWhenSourceIsCancelled() throws Exception {
        try (CancellationTokenSource source = new CancellationTokenSource()) {
            AtomicInteger calls = new AtomicInteger();
            try (AutoCloseable ignored = source.token().register(calls::incrementAndGet)) {
                assertEquals(0, calls.get());
                source.cancel();
                assertEquals(1, calls.get());
                assertTrue(source.token().isCancellationRequested());
            }
        }
    }

    @Test
    void registrationAfterCancellationRunsImmediately() throws Exception {
        try (CancellationTokenSource source = new CancellationTokenSource()) {
            source.cancel();
            AtomicInteger calls = new AtomicInteger();
            try (AutoCloseable ignored = source.token().register(calls::incrementAndGet)) {
                assertEquals(1, calls.get());
            }
        }
    }

    @Test
    void linkedSourcePropagatesCancellationAfterLinkCreation() {
        try (CancellationTokenSource caller = new CancellationTokenSource();
             CancellationTokenSource linked = CancellationTokenSource.createLinkedTokenSource(caller.token())) {
            assertFalse(linked.isCancellationRequested());
            caller.cancel();
            assertTrue(linked.isCancellationRequested());
            assertThrows(java.util.concurrent.CancellationException.class, linked.token()::throwIfCancellationRequested);
        }
    }

    @Test
    void closingRegistrationPreventsCallback() throws Exception {
        try (CancellationTokenSource source = new CancellationTokenSource()) {
            AtomicInteger calls = new AtomicInteger();
            AutoCloseable registration = source.token().register(calls::incrementAndGet);
            registration.close();
            source.cancel();
            assertEquals(0, calls.get());
        }
    }
}
