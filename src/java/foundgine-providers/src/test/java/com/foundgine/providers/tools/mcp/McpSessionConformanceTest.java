package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.Options;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.atomic.AtomicLong;
import org.junit.jupiter.api.Test;

/** Runs the shared {@code mcp-session-scenarios.json} (spec mcp-session-lifecycle-v1); the C# runner runs the same file. */
class McpSessionConformanceTest {
    @Test void sharedScenariosBehaveIdenticallyInJava() throws Exception {
        JsonNode root = new ObjectMapper().readTree(Files.readString(Path.of("../../conformance/mcp-session-scenarios.json")));
        assertEquals("mcp-session-lifecycle-v1", root.path("spec").asText());
        assertTrue(root.path("scenarios").size() > 0);
        for (JsonNode sc : root.path("scenarios")) run(sc);
    }

    private static String owner(JsonNode step) { return step.hasNonNull("owner") ? step.get("owner").asText() : null; }

    /** Message-carrying equality that only needs the two-argument JUnit overload. */
    private static void same(Object expected, Object actual, String where) {
        if (!expected.equals(actual)) throw new AssertionError(where + ": expected <" + expected + "> but was <" + actual + ">");
    }

    private static void run(JsonNode sc) {
        String name = sc.path("name").asText();
        var now = new AtomicLong(sc.path("startUnixMs").asLong());
        var manager = new FoundgineMcpSessionManager(
                new Options(Duration.ofMillis(sc.path("idleTimeoutMs").asLong()),
                        Duration.ofMillis(sc.path("maximumLifetimeMs").asLong()), sc.path("maximumActiveSessions").asInt()),
                new FoundgineMcpSessionManager.InMemoryStore(), now::get);
        Map<String, String> ids = new HashMap<>();
        int n = 0;
        for (JsonNode step : sc.path("steps")) {
            String where = name + " step " + (++n) + " (" + step.path("op").asText() + ")";
            String op = step.path("op").asText();
            String id = step.has("session") ? ids.getOrDefault(step.get("session").asText(), step.get("session").asText()) : null;
            switch (op) {
                case "advance" -> now.addAndGet(step.path("ms").asLong());
                case "create" -> {
                    String expect = step.path("expect").asText();
                    try {
                        var s = manager.create(owner(step));
                        same("ok", expect, where);
                        same(step.path("expiresAt").asLong(), s.expiresAtUnixMilliseconds(), where);
                        ids.put(step.path("as").asText(), s.sessionId());
                    } catch (IllegalStateException full) {
                        same("full", expect, where);
                    }
                }
                case "validate" -> {
                    var v = manager.validate(id, owner(step));
                    same(step.path("expect").asText(), v.status().name(), where);
                    if (v.isValid()) same(step.path("expiresAt").asLong(), v.session().expiresAtUnixMilliseconds(), where);
                }
                case "terminate" -> same(step.path("expect").asBoolean(), manager.terminate(id, owner(step)), where);
                case "purge" -> same(step.path("expect").asInt(), manager.purgeExpired(), where);
                case "count" -> same(step.path("expect").asInt(), manager.activeSessionCount(), where);
                default -> throw new AssertionError("unknown op in " + where);
            }
        }
    }
}
