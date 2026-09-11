package com.foundgine.samples.supplychain.advanced.mutation;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.providers.tools.mcp.FoundgineMcpMutationTools;
import com.foundgine.providers.tools.mcp.FoundgineMcpTools;
import com.foundgine.runtime.MutationExecutionResult;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;

import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;

/**
 * Transport-neutral MCP facade for the Advanced sample.
 *
 * MCP remains an external actor boundary: JSON is decoded into semantic
 * commands and then handed to the Runtime mutation pipeline. No MCP-specific
 * concepts leak into the domain mutation services.
 */
public final class AdvancedMcpFacade {
    private final ObjectMapper mapper = new ObjectMapper();
    private final AdvancedMutationPipeline pipeline;
    private final FoundgineMcpTools queryTools;
    private final FoundgineMcpMutationTools mutationTools;

    public AdvancedMcpFacade(SupplyChainData data, Authorization.Context auth) {
        this.pipeline = new AdvancedMutationPipeline(Objects.requireNonNull(data), Objects.requireNonNull(auth));
        this.queryTools = new FoundgineMcpTools(this::query);
        this.mutationTools = new FoundgineMcpMutationTools(this::mutate);
    }

    public FoundgineMcpTools queryTools() { return queryTools; }
    public FoundgineMcpMutationTools mutationTools() { return mutationTools; }

    private CompletionStage<Object> query(String json) {
        try {
            JsonNode request = mapper.readTree(json);
            String tool = requiredText(request, "tool");
            if ("capabilities".equals(tool)) {
                return CompletableFuture.completedFuture(Map.of(
                        "tools", new String[]{"place_order", "cancel_order", "capabilities"},
                        "mutationBoundary", "Foundgine.Runtime",
                        "authorization", "Foundgine semantic authorization"));
            }
            throw new IllegalArgumentException("Unknown Advanced MCP query tool: " + tool);
        } catch (Exception e) {
            return CompletableFuture.failedFuture(e);
        }
    }

    private CompletionStage<Object> mutate(String json) {
        try {
            JsonNode request = mapper.readTree(json);
            String tool = requiredText(request, "tool");
            CompletionStage<MutationExecutionResult> result;
            switch (tool) {
                case "place_order" -> result = pipeline.placeOrder(
                        requiredText(request, "actor"),
                        requiredInt(request, "customerId"),
                        requiredInt(request, "productId"),
                        requiredInt(request, "quantity"),
                        requiredText(request, "idempotencyKey"));
                case "cancel_order" -> result = pipeline.cancelOrder(
                        requiredText(request, "actor"),
                        requiredInt(request, "orderId"),
                        requiredText(request, "idempotencyKey"));
                default -> throw new IllegalArgumentException("Unknown Advanced MCP mutation tool: " + tool);
            }
            return result.thenApply(this::toResponse);
        } catch (Exception e) {
            return CompletableFuture.failedFuture(e);
        }
    }

    private Object toResponse(MutationExecutionResult result) {
        var response = new LinkedHashMap<String, Object>();
        response.put("planFingerprint", result.planFingerprint());
        response.put("resultFingerprint", result.resultFingerprint());
        response.put("approvalId", result.approvalId());
        response.put("approvedBy", result.approvedBy());
        response.put("results", result.result().results());
        return response;
    }

    private static String requiredText(JsonNode node, String name) {
        JsonNode value = node.get(name);
        if (value == null || !value.isTextual() || value.asText().isBlank())
            throw new IllegalArgumentException("Missing required text field: " + name);
        return value.asText();
    }

    private static int requiredInt(JsonNode node, String name) {
        JsonNode value = node.get(name);
        if (value == null || !value.isIntegralNumber())
            throw new IllegalArgumentException("Missing required integer field: " + name);
        return value.intValueExact();
    }
}
