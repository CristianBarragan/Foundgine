package com.foundgine.providers.gateway.diagnostics;

import com.foundgine.providers.gateway.FoundgineGatewayEventType;
import java.util.Map;
import java.util.TreeMap;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.LongAdder;
import java.util.function.BiConsumer;

/**
 * Dependency-free, low-cardinality gateway metrics (the Java counterpart of the .NET Meter/ActivitySource).
 * Hosts bridge to OpenTelemetry/Micrometer by calling {@link #snapshot()} or installing an {@link #observer}.
 * Labels never contain payloads, user/tenant ids or arbitrary subjects.
 */
public final class FoundgineGatewayDiagnostics {
    public static final String INSTRUMENTATION_NAME = "Foundgine.Gateway";
    public static final String INSTRUMENTATION_VERSION = "1.0.0";
    public static final String EVENTS_PUBLISHED = "foundgine.gateway.events.published";
    public static final String HOOK_FAILURES = "foundgine.gateway.hooks.failures";
    public static final String EVENTS_ENQUEUED = "foundgine.gateway.events.enqueued";
    public static final String OUTBOX_MESSAGES = "foundgine.outbox.messages";

    private static final ConcurrentHashMap<String, LongAdder> COUNTERS = new ConcurrentHashMap<>();
    private static final LongAdder PUBLISH_COUNT = new LongAdder();
    private static final LongAdder PUBLISH_NANOS = new LongAdder();
    private static volatile BiConsumer<String, Map<String, String>> observer;

    private FoundgineGatewayDiagnostics() { }

    /** Installs a callback invoked for every counter increment (metric name, labels). Pass null to clear. */
    public static void observer(BiConsumer<String, Map<String, String>> value) { observer = value; }

    /** Immutable view of all counters keyed {@code name{label=value,...}}, sorted for determinism. */
    public static Map<String, Long> snapshot() {
        var result = new TreeMap<String, Long>();
        COUNTERS.forEach((k, v) -> result.put(k, v.sum()));
        result.put("foundgine.gateway.events.publish.count", PUBLISH_COUNT.sum());
        result.put("foundgine.gateway.events.publish.nanos", PUBLISH_NANOS.sum());
        return Map.copyOf(result);
    }

    /** Test support: clears all counters. */
    public static void reset() { COUNTERS.clear(); PUBLISH_COUNT.reset(); PUBLISH_NANOS.reset(); }

    public static void recordPublished(FoundgineGatewayEventType type, String providerId, long elapsedNanos) {
        var labels = new TreeMap<String, String>();
        labels.put("event.type", type.name());
        if (providerId != null && !providerId.isBlank()) labels.put("provider.id", providerId);
        increment(EVENTS_PUBLISHED, labels);
        PUBLISH_COUNT.increment();
        PUBLISH_NANOS.add(Math.max(0, elapsedNanos));
    }
    public static void recordEnqueued(FoundgineGatewayEventType type) {
        increment(EVENTS_ENQUEUED, Map.of("event.type", type.name()));
    }
    public static void recordHookFailure(String hookType, boolean critical) {
        var labels = new TreeMap<String, String>();
        labels.put("hook.type", hookType);
        labels.put("hook.critical", Boolean.toString(critical));
        increment(HOOK_FAILURES, labels);
    }
    /** Outcome is one of: published, retry_scheduled, dead_lettered, loop_error. */
    public static void recordOutbox(String outcome) {
        increment(OUTBOX_MESSAGES, Map.of("outcome", outcome));
    }

    private static void increment(String name, Map<String, String> labels) {
        var key = name + new TreeMap<>(labels);
        COUNTERS.computeIfAbsent(key, k -> new LongAdder()).increment();
        var o = observer;
        if (o != null) {
            try { o.accept(name, Map.copyOf(labels)); } catch (RuntimeException ignored) { /* telemetry never breaks the request path */ }
        }
    }
}
