package com.foundgine.extensions.temporal.observability;

import java.util.Map;
import java.util.TreeMap;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.BiConsumer;

/**
 * Dependency-free counters for the Temporal integration (the C# version exposes an ActivitySource and a Meter).
 * Names match the C# instruments. Bridge to OpenTelemetry/Micrometer with {@link #listener(BiConsumer)}. Tags are
 * low-cardinality only: process and operation names, never tenants or ids.
 */
public final class FoundgineTemporalDiagnostics {
    public static final String NAME = "Foundgine.Temporal";
    public static final String STARTS = "foundgine.temporal.workflows.started";
    public static final String OPERATIONS = "foundgine.temporal.operations";

    private static final Map<String, AtomicLong> COUNTERS = new ConcurrentHashMap<>();
    private static volatile BiConsumer<String, Map<String, String>> listener;

    private FoundgineTemporalDiagnostics() { }

    /** Receives every measurement as (instrument name, tags). Pass null to remove. */
    public static void listener(BiConsumer<String, Map<String, String>> value) { listener = value; }

    /** Outcome: started | duplicate. */
    public static void recordStart(String process, String outcome) {
        add(STARTS, Map.of("process", process, "outcome", outcome));
    }

    public static void recordOperation(String operation, boolean compensation) {
        add(OPERATIONS, Map.of("operation", operation, "compensation", Boolean.toString(compensation)));
    }

    /** Current value of a counter for the given tag pairs (key, value, key, value, ...). */
    public static long count(String name, String... tagPairs) {
        var tags = new TreeMap<String, String>();
        for (int i = 0; i + 1 < tagPairs.length; i += 2) tags.put(tagPairs[i], tagPairs[i + 1]);
        var counter = COUNTERS.get(name + tags);
        return counter == null ? 0 : counter.get();
    }

    private static void add(String name, Map<String, String> tags) {
        COUNTERS.computeIfAbsent(name + new TreeMap<>(tags), k -> new AtomicLong()).incrementAndGet();
        var l = listener;
        if (l != null) l.accept(name, tags);
    }
}
