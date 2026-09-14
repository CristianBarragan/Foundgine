package com.foundgine.samples.supplychain.advanced.mcp;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;
import com.foundgine.samples.supplychain.advanced.ambiguity.AmbiguityConnectionStrings;
import com.foundgine.samples.supplychain.advanced.ambiguity.TopSupplierOverdueOrdersService;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.mutation.AdvancedMcpFacade;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;

import java.io.IOException;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.sql.DriverManager;
import java.util.Objects;
import java.util.Set;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;

/**
 * A minimal, dependency-free MCP (Model Context Protocol) server for the Advanced Supply Chain
 * sample.
 *
 * <p>This is the Java counterpart of the C# {@code MCP.Foundgine} sample project: it hosts the same
 * transport-neutral {@link AdvancedMcpFacade} behind a JSON-RPC-over-HTTP endpoint at {@code /mcp},
 * so that an external MCP client (see {@link AdvancedMcpClientDemo}, or any standard MCP client)
 * can call {@code tools/call} against it.
 *
 * <p>The two generic tools exposed by {@link AdvancedMcpFacade}, plus one hand-registered demo
 * capability, are wired up:
 *
 * <ul>
 *   <li>{@code foundgine_query} — arguments: {@code {"intentJson": "..."}}
 *   <li>{@code foundgine_mutation} — arguments: {@code {"mutationJson": "..."}}
 *   <li>{@code find_top_supplier_overdue_orders} — arguments: {@code {"actor": "...", "state":
 *       "...", "supplierName": "..." (optional)}}. Backed directly by Postgres via {@link
 *       com.foundgine.samples.supplychain.advanced.ambiguity.TopSupplierOverdueOrdersService}, not
 *       by {@link AdvancedMcpFacade} — see {@link #findTopSupplierOverdueOrders}.
 * </ul>
 *
 * <p>Uses only the JDK's built-in {@code com.sun.net.httpserver.HttpServer}, so it needs no
 * additional runtime dependencies beyond Jackson (already a project dependency).
 */
public final class AdvancedMcpServer {
    public static final String DEFAULT_HOST = "localhost";
    public static final int DEFAULT_PORT = 4783;
    public static final String MCP_PATH = "/mcp";

    private final ObjectMapper mapper = new ObjectMapper();
    private final AdvancedMcpFacade facade;
    private final HttpServer httpServer;

    public AdvancedMcpServer(AdvancedMcpFacade facade, String host, int port) throws IOException {
        this.facade = Objects.requireNonNull(facade, "facade");
        this.httpServer = HttpServer.create(new InetSocketAddress(host, port), 0);
        this.httpServer.createContext(MCP_PATH, this::handleMcp);
        this.httpServer.createContext(
                "/health", exchange -> respond(exchange, 200, "{\"status\":\"ok\"}"));
    }

    public int port() {
        return httpServer.getAddress().getPort();
    }

    public void start() {
        httpServer.start();
    }

    public void stop() {
        httpServer.stop(0);
    }

    private void handleMcp(HttpExchange exchange) {
        try {
            if (!"POST".equalsIgnoreCase(exchange.getRequestMethod())) {
                respond(exchange, 405, "{\"error\":\"MCP endpoint only accepts POST\"}");
                return;
            }

            JsonNode request = mapper.readTree(exchange.getRequestBody());
            Object id =
                    request.has("id") ? mapper.treeToValue(request.get("id"), Object.class) : null;
            String method = request.path("method").asText("");

            if (!"tools/call".equals(method)) {
                respondRpcError(exchange, id, -32601, "Unsupported MCP method: " + method);
                return;
            }

            JsonNode params = request.path("params");
            String toolName = params.path("name").asText("");
            JsonNode arguments = params.path("arguments");

            CompletionStage<String> resultJson =
                    switch (toolName) {
                        case "foundgine_query" ->
                                facade.queryTools()
                                        .foundgineQuery(arguments.path("intentJson").asText("{}"));
                        case "foundgine_mutation" ->
                                facade.mutationTools()
                                        .foundgineMutation(
                                                arguments.path("mutationJson").asText("{}"))
                                        .thenApply(this::toJson);
                        case "find_top_supplier_overdue_orders" ->
                                findTopSupplierOverdueOrders(arguments);
                        default -> null;
                    };

            if (resultJson == null) {
                respondRpcToolError(exchange, id, "Unknown MCP tool: " + toolName);
                return;
            }

            resultJson
                    .whenComplete(
                            (json, error) -> {
                                try {
                                    if (error != null) {
                                        respondRpcToolError(exchange, id, rootMessage(error));
                                    } else {
                                        respondRpcToolSuccess(exchange, id, json);
                                    }
                                } catch (IOException io) {
                                    exchange.close();
                                }
                            })
                    .toCompletableFuture()
                    .join();
        } catch (Exception e) {
            try {
                respondRpcError(exchange, null, -32603, rootMessage(e));
            } catch (IOException io) {
                exchange.close();
            }
        }
    }

    // The ambiguity-resolution demo capability from the Foundgine walkthrough
    // (docs-site/walkthrough/index.html): "top supplier in <state>" is not a
    // database key, so it is resolved through ranked candidates + evidence
    // rather than guessed. Unlike foundgine_query/foundgine_mutation above,
    // this is a hand-registered tool that talks directly to Postgres via
    // TopSupplierOverdueOrdersService, exactly as the C# MCP.Foundgine
    // sample registers it outside the generic Foundgine dispatch. Requires
    // FOUNDGINE_POSTGRES_CONNECTION_STRING (see AmbiguityConnectionStrings);
    // if it's unset, only this one tool fails — the rest of the server
    // (which runs against in-memory SupplyChainData) is unaffected.
    private CompletionStage<String> findTopSupplierOverdueOrders(JsonNode arguments) {
        String jdbcUrl = AmbiguityConnectionStrings.jdbcUrl("FOUNDGINE_POSTGRES_CONNECTION_STRING");
        if (jdbcUrl == null) {
            return CompletableFuture.failedFuture(
                    new IllegalStateException(
                            "FOUNDGINE_POSTGRES_CONNECTION_STRING is required for"
                                    + " find_top_supplier_overdue_orders."));
        }

        String actor = arguments.path("actor").asText("");
        String state = arguments.path("state").asText("");
        String supplierName =
                arguments.hasNonNull("supplierName")
                        ? arguments.path("supplierName").asText()
                        : null;

        try (var connection = DriverManager.getConnection(jdbcUrl)) {
            var service = new TopSupplierOverdueOrdersService(connection);
            var result = service.findTopSupplierOverdueOrders(actor, state, supplierName);
            return CompletableFuture.completedFuture(toJson(result));
        } catch (Exception e) {
            return CompletableFuture.failedFuture(e);
        }
    }

    private String toJson(Object value) {
        try {
            return mapper.writeValueAsString(value);
        } catch (Exception e) {
            throw new IllegalStateException("Unable to serialize MCP tool result", e);
        }
    }

    private static String rootMessage(Throwable t) {
        Throwable cursor = t;
        while (cursor.getCause() != null) {
            cursor = cursor.getCause();
        }
        return cursor.getMessage() != null ? cursor.getMessage() : cursor.toString();
    }

    private void respondRpcToolSuccess(HttpExchange exchange, Object id, String textPayload)
            throws IOException {
        ObjectNode envelope = mapper.createObjectNode();
        envelope.put("jsonrpc", "2.0");
        envelope.set("id", mapper.valueToTree(id));
        ObjectNode result = envelope.putObject("result");
        result.put("isError", false);
        ObjectNode content = result.putArray("content").addObject();
        content.put("type", "text");
        content.put("text", textPayload);
        respond(exchange, 200, mapper.writeValueAsString(envelope));
    }

    private void respondRpcToolError(HttpExchange exchange, Object id, String message)
            throws IOException {
        ObjectNode envelope = mapper.createObjectNode();
        envelope.put("jsonrpc", "2.0");
        envelope.set("id", mapper.valueToTree(id));
        ObjectNode result = envelope.putObject("result");
        result.put("isError", true);
        ObjectNode content = result.putArray("content").addObject();
        content.put("type", "text");
        content.put("text", message);
        respond(exchange, 200, mapper.writeValueAsString(envelope));
    }

    private void respondRpcError(HttpExchange exchange, Object id, int code, String message)
            throws IOException {
        ObjectNode envelope = mapper.createObjectNode();
        envelope.put("jsonrpc", "2.0");
        envelope.set("id", mapper.valueToTree(id));
        ObjectNode error = envelope.putObject("error");
        error.put("code", code);
        error.put("message", message);
        respond(exchange, 200, mapper.writeValueAsString(envelope));
    }

    private void respond(HttpExchange exchange, int status, String body) throws IOException {
        byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
        exchange.getResponseHeaders().add("Content-Type", "application/json");
        exchange.sendResponseHeaders(status, bytes.length);
        try (OutputStream os = exchange.getResponseBody()) {
            os.write(bytes);
        }
    }

    /** Runs the demo server against the seeded sample data set. */
    public static void main(String[] args) throws IOException {
        var data = SupplyChainData.seed();
        var auth =
                new Authorization.Context(
                        "tenant-a", Set.of(1, 2), Authorization.Role.SUPPLY_CHAIN_MANAGER, false);
        var facade = new AdvancedMcpFacade(data, auth);

        int port = args.length > 0 ? Integer.parseInt(args[0]) : DEFAULT_PORT;
        var server = new AdvancedMcpServer(facade, DEFAULT_HOST, port);
        server.start();

        System.out.println("Foundgine Advanced Supply Chain — MCP server");
        System.out.println("=============================================");
        System.out.println("Listening on http://" + DEFAULT_HOST + ":" + server.port() + MCP_PATH);
        System.out.println(
                "Health check: http://" + DEFAULT_HOST + ":" + server.port() + "/health");
    }
}
