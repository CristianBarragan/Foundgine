package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

/** Runs the shared {@code mcp-sse-transport-scenarios.json} (spec mcp-sse-transport-v1); the C# runner runs the same file. */
class McpSseTransportConformanceTest {
    @Test void sharedScenariosBehaveIdenticallyInJava() throws Exception {
        JsonNode root = new ObjectMapper().readTree(Files.readString(Path.of("../../conformance/mcp-sse-transport-scenarios.json")));
        assertEquals("mcp-sse-transport-v1", root.path("spec").asText());
        assertTrue(root.path("scenarios").size() > 0);
        for (JsonNode sc : root.path("scenarios")) run(sc);
    }

    private static final class RecordingSink implements FoundgineMcpSseStream.Sink {
        final List<String> frames = new ArrayList<>();
        boolean completed;
        @Override public void write(String frame) { frames.add(frame); }
        @Override public void complete() { completed = true; }
    }

    private static String text(JsonNode step, String field) { return step.hasNonNull(field) ? step.get(field).asText() : null; }

    private static void run(JsonNode sc) {
        String name = sc.path("name").asText();
        var origins = new ArrayList<String>();
        sc.path("origins").forEach(o -> origins.add(o.asText()));
        var audit = new ArrayList<String>();
        FoundgineMcpAuditSink sink = e -> {
            String type = e.type().name().replace("_", "").toLowerCase();
            if (type.startsWith("sse")) audit.add(type + ":" + e.actor());
        };
        var gate = new FoundgineMcpSessionGate(new FoundgineMcpSessionManager(), sink, System::currentTimeMillis);
        var registry = new FoundgineMcpSseRegistry(1000, sc.path("maxEvents").asInt(), 1_000_000L);
        var settings = new FoundgineMcpSseTransport.Settings(origins, 0,
                new FoundgineMcpSseLimits(sc.path("maxTotal").asInt(), sc.path("maxPerOwner").asInt()), sink);
        var queue = new ArrayDeque<Runnable>();
        java.util.concurrent.Executor executor = queue::add;
        var ids = new HashMap<String, String>();
        var streams = new HashMap<String, FoundgineMcpSseStream>();
        var sinks = new HashMap<String, RecordingSink>();
        int n = 0;
        for (JsonNode step : sc.path("steps")) {
            String op = step.path("op").asText();
            String where = name + " step " + (++n) + " (" + op + ")";
            String handle = text(step, "session");
            String sessionId = handle == null ? null : ids.getOrDefault(handle, handle);
            String owner = text(step, "owner");
            switch (op) {
                case "session" -> {
                    var outcome = gate.admitPost("initialize", null, owner);
                    assertEquals(200, outcome.httpStatus(), where);
                    ids.put(step.path("as").asText(), outcome.issuedSessionId());
                    registry.stream(outcome.issuedSessionId());
                }
                case "publish" -> registry.stream(sessionId).append(step.path("data").asText());
                case "get" -> {
                    var rec = new RecordingSink();
                    var result = FoundgineMcpSseTransport.get(gate, registry, settings, text(step, "origin"), sessionId, owner,
                            text(step, "lastEventId"), rec, executor);
                    assertEquals(step.path("expect").asInt(), result.status(), where);
                    if (result.status() == 200) {
                        streams.put(step.path("as").asText(), result.stream());
                        sinks.put(step.path("as").asText(), rec);
                        result.stream().start();
                    }
                }
                case "delete" -> assertEquals(step.path("expect").asInt(),
                        FoundgineMcpSseTransport.delete(gate, registry, settings, text(step, "origin"), sessionId, owner), where);
                case "heartbeat" -> streams.get(step.path("stream").asText()).heartbeat();
                case "frames" -> {
                    pump(queue);
                    var rec = sinks.get(step.path("stream").asText());
                    var expected = new ArrayList<String>();
                    step.path("expect").forEach(f -> expected.add(f.asText()));
                    assertEquals(expected, rec.frames, where);
                    rec.frames.clear();
                }
                case "closed" -> {
                    pump(queue);
                    assertEquals(step.path("expect").asBoolean(), sinks.get(step.path("stream").asText()).completed, where);
                }
                case "audit" -> {
                    pump(queue);
                    var expected = new ArrayList<String>();
                    step.path("expect").forEach(f -> expected.add(f.asText()));
                    assertEquals(expected, audit, where);
                    audit.clear();
                }
                default -> throw new AssertionError("unknown op in " + where);
            }
            pump(queue);
        }
    }

    private static void pump(ArrayDeque<Runnable> queue) {
        Runnable r;
        while ((r = queue.poll()) != null) r.run();
    }
}
