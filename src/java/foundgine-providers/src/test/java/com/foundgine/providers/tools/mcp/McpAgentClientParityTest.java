package com.foundgine.providers.tools.mcp;

import com.foundgine.providers.tools.mcp.client.FoundgineMcpAgentClient;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.intent.ReadSelection;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.URI;
import java.net.http.HttpClient;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;

import static org.junit.jupiter.api.Assertions.*;

class McpAgentClientParityTest {
    @Test
    void discoveryThenDynamicIntentThenExecutionUsesDiscoveredContract() throws Exception {
        AtomicInteger calls = new AtomicInteger();
        try (TestServer server = new TestServer(exchange -> {
            String body = read(exchange);
            int call = calls.incrementAndGet();
            if (body.contains("foundgine_capabilities")) {
                reply(exchange, rpc("{\"version\":1,\"capabilities\":[{\"id\":\"Customer.read\",\"name\":\"Read Customer\",\"targetEntityId\":1,\"access\":\"ALLOWED\",\"inputs\":[],\"constraints\":[],\"effects\":[],\"fields\":[\"Id\",\"Name\"],\"relationships\":[\"Transactions\"],\"operation\":\"read\",\"hasSideEffects\":false,\"isIdempotent\":false,\"version\":1,\"requiredSecurityInvariants\":[]}]}));
            } else {
                assertTrue(body.contains("foundgine_query"));
                assertTrue(body.contains("Customer"));
                assertTrue(body.contains("Transactions"));
                assertFalse(body.toLowerCase().contains("security"));
                reply(exchange, rpc("{\"rows\":[]}"));
            }
        })) {
            var client = new FoundgineMcpAgentClient(HttpClient.newHttpClient(), server.uri());
            var result = client.discoverAndExecute(contract -> {
                assertEquals(1, contract.capabilities().size());
                return new ReadIntent("Customer", java.util.List.of(
                        new ReadSelection("Id"),
                        new ReadSelection(null, "Transactions", java.util.List.of(new ReadSelection("Id")))));
            }).toCompletableFuture().get(5, TimeUnit.SECONDS);
            assertEquals(0, result.get("rows").size());
            assertEquals(2, calls.get());
        }
    }

    @Test
    void sseToolResultIsUnwrapped() throws Exception {
        try (TestServer server = new TestServer(exchange ->
                reply(exchange, "event: message\ndata: " + rpc("{\"ok\":true}") + "\n\n"))) {
            var client = new FoundgineMcpAgentClient(HttpClient.newHttpClient(), server.uri());
            var result = client.executeQuery(new ReadIntent("Customer", java.util.List.of(new ReadSelection("Id"))))
                    .toCompletableFuture().get(5, TimeUnit.SECONDS);
            assertTrue(result.get("ok").asBoolean());
        }
    }

    @Test
    void mcpErrorIsNotTreatedAsSuccess() throws Exception {
        try (TestServer server = new TestServer(exchange ->
                reply(exchange, "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"message\":\"denied\"}}"))) {
            var client = new FoundgineMcpAgentClient(HttpClient.newHttpClient(), server.uri());
            var ex = assertThrows(Exception.class, () -> client.executeQuery(
                    new ReadIntent("Customer", java.util.List.of(new ReadSelection("Id"))))
                    .toCompletableFuture().get(5, TimeUnit.SECONDS));
            assertTrue(ex.getMessage().toLowerCase().contains("denied"));
        }
    }

    private static String rpc(String nested) {
        return "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"content\":[{\"type\":\"text\",\"text\":"
                + quote(nested) + "}]}}";
    }

    private static String quote(String value) {
        return '"' + value.replace("\\", "\\\\").replace("\"", "\\\"") + '"';
    }

    private static String read(HttpExchange exchange) throws IOException {
        return new String(exchange.getRequestBody().readAllBytes(), StandardCharsets.UTF_8);
    }

    private static void reply(HttpExchange exchange, String body) throws IOException {
        byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
        exchange.sendResponseHeaders(200, bytes.length);
        try (var out = exchange.getResponseBody()) { out.write(bytes); }
    }

    private static final class TestServer implements AutoCloseable {
        private final HttpServer server;
        TestServer(Handler handler) throws IOException {
            server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
            server.createContext("/mcp", handler::handle);
            server.start();
        }
        URI uri() { return URI.create("http://127.0.0.1:" + server.getAddress().getPort() + "/mcp"); }
        @Override public void close() { server.stop(0); }
    }

    @FunctionalInterface
    private interface Handler { void handle(HttpExchange exchange) throws IOException; }
}
