package com.foundgine.providers.tools.mcp;

import static com.foundgine.providers.tools.mcp.McpAdapterParityTest.Engine;
import static com.foundgine.providers.tools.mcp.McpAdapterParityTest.host;
import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.intent.ReadSelection;
import com.foundgine.providers.tools.mcp.client.FoundgineMcpAgentClient;
import com.sun.net.httpserver.HttpServer;
import java.net.InetSocketAddress;
import java.net.URI;
import java.net.http.HttpClient;
import java.nio.charset.StandardCharsets;
import java.util.List;
import org.junit.jupiter.api.Test;

/** The Java agent client talking to the Java MCP server core over real loopback HTTP. */
class McpClientServerRoundTripParityTest {
    @Test
    void clientDiscoversCapabilitiesAndExecutesThroughTheServerWithHostSecurity() throws Exception {
        var engine = new Engine();
        var ctx = host("host-subject");
        var tools = FoundgineMcpTools.builder(engine.proxy).securityContextFactory(() -> ctx).build();
        var mcp = FoundgineMcpServer.builder().withReadTools(tools).build();

        var http = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        http.createContext("/mcp", ex -> {
            var body = new String(ex.getRequestBody().readAllBytes(), StandardCharsets.UTF_8);
            var reply = mcp.handleAsync(body).toCompletableFuture().join();
            if (reply.isEmpty()) {
                ex.sendResponseHeaders(202, -1);
            } else {
                var bytes = reply.get().getBytes(StandardCharsets.UTF_8);
                ex.getResponseHeaders().add("Content-Type", "application/json");
                ex.sendResponseHeaders(200, bytes.length);
                ex.getResponseBody().write(bytes);
            }
            ex.close();
        });
        http.start();
        try {
            var client = new FoundgineMcpAgentClient(HttpClient.newHttpClient(),
                    URI.create("http://127.0.0.1:" + http.getAddress().getPort() + "/mcp"));

            var contract = client.discoverCapabilities().toCompletableFuture().get();
            assertEquals(1, contract.version());
            assertSame(ctx, engine.discovery.get(0));

            var intent = new ReadIntent("Order", List.of(new ReadSelection("id", null, List.of())));
            var result = client.executeQuery(intent).toCompletableFuture().get();
            assertEquals(7, result.get("rows").get(0).get("id").asInt());
            assertEquals("Order", engine.intents.get(0).rootEntity());
            assertSame(ctx, engine.intents.get(0).security());
        } finally {
            http.stop(0);
        }
    }
}
