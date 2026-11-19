package com.foundgine.samples.supplychain.advanced.mcp;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.samples.supplychain.advanced.intent.IntentCaller;
import com.foundgine.samples.supplychain.advanced.intent.SupplyChainIntentService;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;

import java.io.IOException;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;

/**
 * HTTP and MCP surface for the canonical intent boundary ({@link SupplyChainIntentService}).
 *
 * <p>There is one request shape. The body of {@code POST /intent} is the canonical read intent
 * itself (see {@code JsonReadIntentAdapter}), for example:
 *
 * <pre>{@code
 * {"rootEntity":"Supplier",
 *  "selections":[{"field":"Name"},{"field":"RiskScore"}],
 *  "filter":{"kind":"field","field":"Country","operator":"Eq","value":"NZ"}}
 * }</pre>
 *
 * <ul>
 *   <li>{@code POST /intent}: execute it as the caller
 *   <li>{@code POST /intent/explain}: how it resolves, what the caller's policy leaves of it, and
 *       the authorized plan; nothing is executed
 *   <li>{@code GET /intent/capabilities}: what the caller may read, by entity, field, relationship
 * </ul>
 *
 * <p>The caller (tenant and role) comes from the {@code X-Foundgine-Tenant} and {@code
 * X-Foundgine-Role} headers and never from the body. DEMO ONLY: a real host derives identity from
 * an authenticated principal; see {@link IntentCaller}.
 *
 * <p>MCP: the {@code foundgine_intent} tool takes {@code {"intent": {...}, "mode":
 * "execute|explain|capabilities"}} and uses the same service, with the caller taken from the same
 * headers of the {@code /mcp} request.
 */
public final class AdvancedIntentEndpoint {
    public static final String PATH = "/intent";
    public static final int MAX_BODY_BYTES = 65_536;

    private static final ObjectMapper JSON = new ObjectMapper();

    private final SupplyChainIntentService service;

    public AdvancedIntentEndpoint(SupplyChainIntentService service) {
        this.service = Objects.requireNonNull(service, "service");
    }

    public void register(HttpServer server) {
        server.createContext(PATH, this::handle);
    }

    // ------------------------------------------------------------------ HTTP

    private void handle(HttpExchange exchange) throws IOException {
        try (exchange) {
            String path = exchange.getRequestURI().getPath();
            String method = exchange.getRequestMethod();
            try {
                switch (path) {
                    case PATH -> {
                        if (!requireMethod(exchange, method, "POST")) return;
                        var caller = caller(exchange);
                        respond(exchange, 200, service.execute(caller, readBody(exchange)).toCompletableFuture().join());
                    }
                    case PATH + "/explain" -> {
                        if (!requireMethod(exchange, method, "POST")) return;
                        var caller = caller(exchange);
                        respond(exchange, 200, service.explain(caller, readBody(exchange)));
                    }
                    case PATH + "/capabilities" -> {
                        if (!requireMethod(exchange, method, "GET")) return;
                        respond(exchange, 200, service.capabilities(caller(exchange)));
                    }
                    default -> respondError(exchange, 404, "not_found", "Unknown intent path.");
                }
            } catch (PayloadTooLarge e) {
                respondError(exchange, 413, "payload_too_large", e.getMessage());
            } catch (Throwable t) {
                var failure = SupplyChainIntentService.failure(t);
                respondError(exchange, failure.status(), failure.type(), failure.message());
            }
        }
    }

    private static boolean requireMethod(HttpExchange exchange, String actual, String expected)
            throws IOException {
        if (expected.equalsIgnoreCase(actual)) return true;
        exchange.getResponseHeaders().add("Allow", expected);
        respondError(exchange, 405, "method_not_allowed", "Use " + expected + " for this path.");
        return false;
    }

    /** The caller for a request, from headers only. */
    public IntentCaller caller(HttpExchange exchange) {
        var headers = exchange.getRequestHeaders();
        return IntentCaller.resolve(
                headers.getFirst(IntentCaller.TENANT_HEADER),
                headers.getFirst(IntentCaller.ROLE_HEADER),
                service.knownTenants());
    }

    private static String readBody(HttpExchange exchange) throws IOException {
        byte[] bytes = exchange.getRequestBody().readNBytes(MAX_BODY_BYTES + 1);
        if (bytes.length > MAX_BODY_BYTES) {
            throw new PayloadTooLarge("An intent may not exceed " + MAX_BODY_BYTES + " bytes.");
        }
        return new String(bytes, StandardCharsets.UTF_8);
    }

    private static final class PayloadTooLarge extends RuntimeException {
        PayloadTooLarge(String message) {
            super(message);
        }
    }

    private static void respond(HttpExchange exchange, int status, Object body) throws IOException {
        write(exchange, status, JSON.writeValueAsBytes(body));
    }

    private static void respondError(HttpExchange exchange, int status, String type, String message)
            throws IOException {
        var error = new LinkedHashMap<String, Object>();
        error.put("type", type);
        error.put("message", message);
        respond(exchange, status, Map.of("error", error));
    }

    private static void write(HttpExchange exchange, int status, byte[] bytes) throws IOException {
        exchange.getResponseHeaders().set("Content-Type", "application/json");
        exchange.sendResponseHeaders(status, bytes.length);
        try (OutputStream os = exchange.getResponseBody()) {
            os.write(bytes);
        }
    }

    // ------------------------------------------------------------------ MCP

    /**
     * Handles the {@code foundgine_intent} MCP tool. Completes with the JSON text of the result, or
     * fails with the underlying exception (classify it with {@link
     * SupplyChainIntentService#failure}).
     */
    public CompletionStage<String> callTool(IntentCaller caller, JsonNode arguments) {
        try {
            String mode = arguments.path("mode").asText("execute");
            JsonNode intent = arguments.get("intent");
            Object result;
            switch (mode) {
                case "capabilities" -> result = service.capabilities(caller);
                case "explain" -> result = service.explain(caller, intentText(intent));
                case "execute" -> {
                    return service.execute(caller, intentText(intent)).thenApply(AdvancedIntentEndpoint::toJson);
                }
                default -> throw new IllegalArgumentException("Unknown mode '" + mode + "'. Use execute, explain or capabilities.");
            }
            return CompletableFuture.completedFuture(toJson(result));
        } catch (Throwable t) {
            return CompletableFuture.failedFuture(t);
        }
    }

    /** The intent as a JSON string, from either an object or an already-serialized string. */
    private static String intentText(JsonNode intent) {
        if (intent == null || intent.isNull() || intent.isMissingNode()) {
            throw new IllegalArgumentException("Missing 'intent'.");
        }
        return intent.isTextual() ? intent.asText() : intent.toString();
    }

    private static String toJson(Object value) {
        try {
            return JSON.writeValueAsString(value);
        } catch (IOException e) {
            throw new IllegalStateException("Unable to serialize the intent result.", e);
        }
    }
}
