package com.foundgine.samples.supplychain.advanced.mcp;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * A runnable MCP client for the Advanced Supply Chain sample.
 *
 * <p>This is the Java counterpart of the C# {@code Semantic/McpClient} console client: it speaks
 * plain JSON-RPC {@code tools/call} over HTTP to {@link AdvancedMcpServer}, with no SDK dependency,
 * exactly like the C# sample's raw-HTTP client. Run {@link AdvancedMcpServer} first, then run this
 * class (optionally passing the MCP endpoint URL as the first argument).
 */
public final class AdvancedMcpClientDemo {
    private static final String MCP_PROTOCOL_VERSION = "2025-06-18";

    private final HttpClient client =
            HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(5)).build();
    private final ObjectMapper mapper = new ObjectMapper();
    private final URI endpoint;
    private final AtomicInteger requestId = new AtomicInteger();

    public AdvancedMcpClientDemo(URI endpoint) {
        this.endpoint = endpoint;
    }

    public static void main(String[] args) throws Exception {
        URI endpoint =
                URI.create(
                        args.length > 0
                                ? args[0]
                                : "http://"
                                        + AdvancedMcpServer.DEFAULT_HOST
                                        + ":"
                                        + AdvancedMcpServer.DEFAULT_PORT
                                        + AdvancedMcpServer.MCP_PATH);

        System.out.println("Foundgine Advanced Supply Chain — MCP client");
        System.out.println("=============================================");
        System.out.println("Endpoint: " + endpoint);
        System.out.println();

        var demo = new AdvancedMcpClientDemo(endpoint);

        demo.report("capabilities", demo.callQuery("{\"tool\":\"capabilities\"}"));

        var placed =
                demo.callMutation(
                        "{\"tool\":\"place_order\",\"actor\":\"alice\",\"customerId\":1,\"productId\":4,"
                            + "\"quantity\":2,\"idempotencyKey\":\"mcp-client-demo-place-1\"}");
        demo.report("place_order", placed);

        int orderId = extractFirstReturnedValue(placed);
        var replay =
                demo.callMutation(
                        "{\"tool\":\"place_order\",\"actor\":\"alice\",\"customerId\":1,\"productId\":4,"
                            + "\"quantity\":2,\"idempotencyKey\":\"mcp-client-demo-place-1\"}");
        demo.report("place_order (idempotent replay)", replay);

        var cancelled =
                demo.callMutation(
                        "{\"tool\":\"cancel_order\",\"actor\":\"alice\",\"orderId\":"
                                + orderId
                                + ",\"idempotencyKey\":\"mcp-client-demo-cancel-1\"}");
        demo.report("cancel_order", cancelled);
    }

    /** Calls the read-only {@code foundgine_query} tool. */
    public JsonNode callQuery(String intentJson) throws Exception {
        ObjectNode arguments = mapper.createObjectNode();
        arguments.put("intentJson", intentJson);
        return callTool("foundgine_query", arguments);
    }

    /** Calls the mutating {@code foundgine_mutation} tool. */
    public JsonNode callMutation(String mutationJson) throws Exception {
        ObjectNode arguments = mapper.createObjectNode();
        arguments.put("mutationJson", mutationJson);
        return callTool("foundgine_mutation", arguments);
    }

    private JsonNode callTool(String name, ObjectNode arguments) throws Exception {
        ObjectNode payload = mapper.createObjectNode();
        payload.put("jsonrpc", "2.0");
        payload.put("id", requestId.incrementAndGet());
        payload.put("method", "tools/call");
        ObjectNode params = payload.putObject("params");
        params.put("name", name);
        params.set("arguments", arguments);

        HttpRequest request =
                HttpRequest.newBuilder(endpoint)
                        .header("Content-Type", "application/json")
                        .header("Accept", "application/json, text/event-stream")
                        .header("MCP-Protocol-Version", MCP_PROTOCOL_VERSION)
                        .POST(
                                HttpRequest.BodyPublishers.ofString(
                                        mapper.writeValueAsString(payload)))
                        .build();

        HttpResponse<String> response = client.send(request, HttpResponse.BodyHandlers.ofString());
        if (response.statusCode() / 100 != 2) {
            throw new IllegalStateException(
                    "MCP request failed with " + response.statusCode() + ": " + response.body());
        }
        return mapper.readTree(response.body());
    }

    private void report(String label, JsonNode rpcResponse) {
        JsonNode result = rpcResponse.path("result");
        boolean isError = result.path("isError").asBoolean(false);
        String text =
                result.path("content").isArray() && result.path("content").size() > 0
                        ? result.path("content").get(0).path("text").asText("")
                        : rpcResponse.toString();
        System.out.println("[" + (isError ? "ERROR" : "OK") + "] " + label);
        System.out.println("  " + text);
    }

    private static int extractFirstReturnedValue(JsonNode rpcResponse) {
        try {
            String text = rpcResponse.path("result").path("content").get(0).path("text").asText();
            ObjectMapper mapper = new ObjectMapper();
            JsonNode payload = mapper.readTree(text);
            JsonNode results = payload.path("results");
            JsonNode returnedValues = results.get(0).path("returnedValues");
            return returnedValues.fields().next().getValue().asInt();
        } catch (Exception e) {
            throw new IllegalStateException(
                    "Unable to extract order id from place_order response", e);
        }
    }
}
