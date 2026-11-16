package com.foundgine.langchain4j;

import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.providers.tools.mcp.FoundgineMcpServer;
import dev.langchain4j.agent.tool.ToolExecutionRequest;
import java.util.concurrent.CompletableFuture;
import org.junit.jupiter.api.Test;

/** Passes against LangChain4j 1.0.0 sources compiled with javac 21 (not via Maven). */
class FoundgineLangChain4jToolsTest {
    private static final ObjectMapper M = new ObjectMapper();

    private static FoundgineMcpServer server() {
        var schema = M.createObjectNode().put("type", "object");
        schema.putObject("properties").putObject("q").put("type", "string").put("description", "query");
        schema.putArray("required").add("q");
        return FoundgineMcpServer.builder()
                .addTool(new FoundgineMcpServer.Tool("echo", "Echo", schema, true,
                        (args, ct) -> CompletableFuture.completedFuture("got " + args.get("q").asText())))
                .addTool(new FoundgineMcpServer.Tool("boom", "Boom", M.createObjectNode().put("type", "object"), true,
                        (args, ct) -> CompletableFuture.failedFuture(new IllegalStateException("secret internals"))))
                .build();
    }

    @Test
    void specificationsMirrorTheMcpTools() {
        var map = FoundgineLangChain4jTools.toolMap(server());
        var names = map.keySet().stream().map(s -> s.name()).toList();
        assertEquals(java.util.List.of("echo", "boom"), names);
    }

    @Test
    void executesAndHidesInternalFailures() {
        var map = FoundgineLangChain4jTools.toolMap(server());
        for (var e : map.entrySet()) {
            var name = e.getKey().name();
            var request = ToolExecutionRequest.builder().id("1").name(name).arguments("{\"q\":\"hi\"}").build();
            var out = e.getValue().execute(request, "mem");
            if (name.equals("echo")) assertEquals("got hi", out);
            else assertFalse(out.contains("secret"), out);
        }
    }
}
