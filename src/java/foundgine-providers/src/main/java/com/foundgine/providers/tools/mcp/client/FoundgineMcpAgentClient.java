package com.foundgine.providers.tools.mcp.client;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;
import com.foundgine.core.semantic.capabilities.SemanticCapabilityContract;
import com.foundgine.core.semantic.intent.ReadIntent;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.util.Objects;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.function.Function;

/**
 * Provider-neutral MCP client for the Foundgine capability-discovery workflow.
 *
 * <p>The client is deliberately transport-only. It discovers the host-visible
 * capability contract, lets the caller construct a dynamic {@link ReadIntent},
 * and sends that intent to the canonical Foundgine MCP query tool. Security
 * context is never copied into the serialized intent by this client.</p>
 */
public final class FoundgineMcpAgentClient {
    private static final String MCP_PROTOCOL_VERSION = "2025-06-18";
    private final HttpClient client;
    private final URI endpoint;
    private final ObjectMapper mapper = new ObjectMapper();
    private final AtomicInteger requestId = new AtomicInteger();

    public FoundgineMcpAgentClient(HttpClient client, URI endpoint) {
        this.client = Objects.requireNonNull(client, "client");
        this.endpoint = Objects.requireNonNull(endpoint, "endpoint");
    }

    /** Backwards-compatible raw transport call. */
    public CompletionStage<String> call(String intentJson) {
        Objects.requireNonNull(intentJson, "intentJson");
        HttpRequest request = HttpRequest.newBuilder(endpoint)
                .header("Content-Type", "application/json")
                .header("Accept", "application/json, text/event-stream")
                .header("MCP-Protocol-Version", MCP_PROTOCOL_VERSION)
                .POST(HttpRequest.BodyPublishers.ofString(intentJson))
                .build();
        return client.sendAsync(request, HttpResponse.BodyHandlers.ofString())
                .thenCompose(response -> response.statusCode() / 100 == 2
                        ? CompletableFuture.completedFuture(response.body())
                        : CompletableFuture.failedFuture(new IllegalStateException(
                                "MCP request failed with " + response.statusCode() + ": " + response.body())));
    }

    /** Discovers the caller-visible semantic capability contract. */
    public CompletionStage<SemanticCapabilityContract> discoverCapabilities() {
        return callTool("foundgine_capabilities", mapper.createObjectNode())
                .thenApply(payload -> readToolResult(payload, SemanticCapabilityContract.class));
    }

    /** Executes a provider-neutral read intent through the canonical MCP query tool. */
    public CompletionStage<JsonNode> executeQuery(ReadIntent intent) {
        Objects.requireNonNull(intent, "intent");
        try {
            ObjectNode arguments = mapper.createObjectNode();
            JsonNode intentNode = mapper.valueToTree(intent);
            if (intentNode.isObject()) {
                ((ObjectNode) intentNode).remove("security");
            }
            arguments.put("intentJson", mapper.writeValueAsString(intentNode));
            return callTool("foundgine_query", arguments)
                    .thenApply(payload -> readToolResult(payload, JsonNode.class));
        } catch (Exception e) {
            return CompletableFuture.failedFuture(new IllegalArgumentException(
                    "Unable to serialize MCP read intent", e));
        }
    }

    /** Discover capabilities, construct an intent from that contract, then execute it. */
    public CompletionStage<JsonNode> discoverAndExecute(
            Function<SemanticCapabilityContract, ReadIntent> intentFactory) {
        Objects.requireNonNull(intentFactory, "intentFactory");
        return discoverCapabilities().thenCompose(contract -> {
            ReadIntent intent = intentFactory.apply(contract);
            if (intent == null) {
                return CompletableFuture.failedFuture(
                        new IllegalStateException("The intent factory returned null."));
            }
            return executeQuery(intent);
        });
    }

    private CompletionStage<JsonNode> callTool(String toolName, ObjectNode arguments) {
        try {
            ObjectNode payload = mapper.createObjectNode();
            payload.put("jsonrpc", "2.0");
            payload.put("id", requestId.incrementAndGet());
            payload.put("method", "tools/call");
            ObjectNode params = payload.putObject("params");
            params.put("name", toolName);
            params.set("arguments", arguments);

            HttpRequest request = HttpRequest.newBuilder(endpoint)
                    .header("Content-Type", "application/json")
                    .header("Accept", "application/json, text/event-stream")
                    .header("MCP-Protocol-Version", MCP_PROTOCOL_VERSION)
                    .POST(HttpRequest.BodyPublishers.ofString(mapper.writeValueAsString(payload)))
                    .build();

            return client.sendAsync(request, HttpResponse.BodyHandlers.ofString())
                    .thenCompose(response -> {
                        if (response.statusCode() / 100 != 2) {
                            return CompletableFuture.failedFuture(new IllegalStateException(
                                    "MCP request failed with " + response.statusCode() + ": " + response.body()));
                        }
                        try {
                            return CompletableFuture.completedFuture(extractJsonPayload(response.body()));
                        } catch (RuntimeException ex) {
                            return CompletableFuture.failedFuture(ex);
                        }
                    });
        } catch (Exception e) {
            return CompletableFuture.failedFuture(new IllegalStateException("Unable to create MCP request", e));
        }
    }

    private <T> T readToolResult(JsonNode rpcResponse, Class<T> type) {
        if (rpcResponse == null || !rpcResponse.isObject()) {
            throw new IllegalStateException("MCP response is not a JSON object.");
        }
        if (rpcResponse.has("error")) {
            throw new IllegalStateException("MCP tool call failed: " + rpcResponse.get("error"));
        }
        JsonNode result = rpcResponse.get("result");
        if (result == null || !result.isObject()) {
            throw new IllegalStateException("MCP response does not contain a result.");
        }
        if (result.path("isError").asBoolean(false)) {
            throw new IllegalStateException("MCP tool call returned an error: " + result);
        }
        JsonNode content = result.get("content");
        if (content == null || !content.isArray()) {
            throw new IllegalStateException("MCP tool result does not contain content.");
        }
        for (JsonNode item : content) {
            JsonNode text = item.get("text");
            if (text == null || !text.isTextual() || text.asText().isBlank()) continue;
            try {
                JsonNode nested = mapper.readTree(text.asText());
                if (type == JsonNode.class) return type.cast(nested);
                return mapper.treeToValue(nested, type);
            } catch (Exception ignored) {
                // Continue looking for another textual content block, matching the C# client.
            }
        }
        throw new IllegalStateException("MCP tool result did not contain a JSON payload.");
    }

    private JsonNode extractJsonPayload(String body) {
        StringBuilder data = new StringBuilder();
        for (String line : body.split("\\R")) {
            String trimmed = line.endsWith("\r") ? line.substring(0, line.length() - 1) : line;
            if (trimmed.startsWith("data:")) {
                String value = trimmed.substring("data:".length()).stripLeading();
                if (!value.isEmpty()) data.append(value);
            }
        }
        String json = data.length() > 0 ? data.toString() : body;
        try {
            return mapper.readTree(json);
        } catch (Exception e) {
            throw new IllegalStateException("MCP response was not valid JSON or SSE JSON.", e);
        }
    }
}
