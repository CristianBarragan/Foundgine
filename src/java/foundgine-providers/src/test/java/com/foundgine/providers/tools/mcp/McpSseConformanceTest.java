package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

/** Runs the shared {@code mcp-sse-scenarios.json} (spec mcp-sse-replay-v1); the C# runner runs the same file. */
class McpSseConformanceTest {
    @Test void sharedScenariosBehaveIdenticallyInJava() throws Exception {
        JsonNode root = new ObjectMapper().readTree(Files.readString(Path.of("../../conformance/mcp-sse-scenarios.json")));
        assertEquals("mcp-sse-replay-v1", root.path("spec").asText());
        assertTrue(root.path("scenarios").size() > 0);
        for (JsonNode sc : root.path("scenarios")) run(sc);
    }

    private static void same(Object expected, Object actual, String where) {
        if (!expected.equals(actual)) throw new AssertionError(where + ": expected <" + expected + "> but was <" + actual + ">");
    }

    private static void run(JsonNode sc) {
        String name = sc.path("name").asText();
        var buffer = new FoundgineMcpSseBuffer(sc.path("maxEvents").asInt(), sc.path("maxBytes").asLong());
        int n = 0;
        var handles = new java.util.HashMap<String, FoundgineMcpSseBuffer.Subscription>();
        var woke = new java.util.concurrent.atomic.AtomicBoolean();
        var told = new ArrayList<String>();
        for (JsonNode step : sc.path("steps")) {
            String op = step.path("op").asText();
            String where = name + " step " + (++n) + " (" + op + ")";
            switch (op) {
                case "append" -> {
                    woke.set(false);
                    try {
                        var e = buffer.append(step.path("data").asText());
                        same("ok", step.path("expect").asText(), where);
                        same(step.path("id").asText(), e.id(), where);
                    } catch (IllegalArgumentException tooBig) {
                        same("rejected", step.path("expect").asText(), where);
                    }
                    same(step.path("woke").asBoolean(), woke.get(), where + " woke");
                }
                case "subscribe" -> {
                    String as = step.path("as").asText();
                    var h = buffer.subscribe(() -> woke.set(true), () -> told.add(as));
                    same(step.path("expect").asText(), h == null ? "busy" : "ok", where);
                    if (h != null) handles.put(step.path("as").asText(), h);
                }
                case "retire" -> {
                    told.clear();
                    buffer.retire();
                    List<String> expected = new ArrayList<>();
                    step.path("retired").forEach(i -> expected.add(i.asText()));
                    same(expected, told, where);
                }
                case "unsubscribe" -> {
                    var h = handles.get(step.path("as").asText());
                    if (h != null) h.close();
                }
                case "replay" -> {
                    var r = buffer.replayAfter(step.hasNonNull("lastEventId") ? step.get("lastEventId").asText() : null);
                    same(step.path("expect").asText(), r.status().name(), where);
                    List<String> ids = new ArrayList<>();
                    r.events().forEach(e -> ids.add(e.id()));
                    List<String> expected = new ArrayList<>();
                    step.path("ids").forEach(i -> expected.add(i.asText()));
                    same(expected, ids, where);
                }
                case "format" -> same(step.path("expect").asText(),
                        FoundgineMcpSseBuffer.format(new FoundgineMcpSseBuffer.Event(step.path("sequence").asLong(), step.path("data").asText())), where);
                default -> throw new AssertionError("unknown op in " + where);
            }
        }
    }
}
