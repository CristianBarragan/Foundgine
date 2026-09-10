package com.foundgine.core.execution;

import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.time.temporal.ChronoUnit;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** Unit tests for the ported {@link ExecutionContext} (no direct C# unit-test file for this type — it's covered indirectly by larger E2E tests not yet portable). */
class ExecutionContextTest {

    @Test
    void tryGetValueReadsFromValuesMap() {
        ExecutionContext context = new ExecutionContext(Map.of("a.b", 1));

        assertTrue(context.tryGetValue("a.b").isPresent());
        assertEquals(1, context.tryGetValue("a.b").get());
        assertFalse(context.tryGetValue("missing").isPresent());
    }

    @Test
    void ensureWithinDeadlinePassesBeforeDeadline() {
        ExecutionContext context = ExecutionContext.EMPTY.withDeadline(Instant.now().plus(1, ChronoUnit.HOURS));

        context.ensureWithinDeadline(); // should not throw
    }

    @Test
    void ensureWithinDeadlineThrowsAfterDeadline() {
        ExecutionContext context = ExecutionContext.EMPTY.withDeadline(Instant.now().minusSeconds(1));

        assertThrows(IllegalStateException.class, context::ensureWithinDeadline);
    }
}
