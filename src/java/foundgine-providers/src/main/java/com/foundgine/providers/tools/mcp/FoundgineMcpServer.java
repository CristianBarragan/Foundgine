package com.foundgine.providers.tools.mcp;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.CancellationTokenSource;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutionException;

/**
 * Transport-neutral MCP server core for the Foundgine tools: JSON-RPC 2.0 over a single request/response channel
 * ({@code initialize}, {@code ping}, {@code tools/list}, {@code tools/call}; notifications get no reply). The C# adapter relies on
 * the ModelContextProtocol SDK for this layer; this class plays that role for Java hosts without adding a dependency. The host
 * owns the HTTP/stdio transport and, above all, authentication: the {@link
 * com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider} given to the tools must read the trusted
 * identity of the <em>current request</em>. Tools capture it synchronously when called, so thread-bound hosts (servlet
 * filters) work. Tool errors become {@code isError} results; only security, validation and not-configured messages reach the
 * agent, anything else is reported generically.
 */
public final class FoundgineMcpServer {
    public static final String LATEST_PROTOCOL_VERSION = "2025-06-18";
    static final List<String> SUPPORTED = List.of(LATEST_PROTOCOL_VERSION, "2025-03-26", "2024-11-05");
    static final int PARSE_ERROR = -32700;
    static final int INVALID_REQUEST = -32600;
    static final int METHOD_NOT_FOUND = -32601;
    static final int INVALID_PARAMS = -32602;

    /** Runs one tool. May throw or return a failed stage; see the class comment for how errors are reported. */
    @FunctionalInterface
    public interface ToolHandler {
        CompletionStage<String> call(JsonNode arguments, CancellationToken cancellationToken);
    }

    public record Tool(String name, String description, ObjectNode inputSchema, boolean readOnly, ToolHandler handler) {
        public Tool {
            Objects.requireNonNull(name, "name");
            Objects.requireNonNull(inputSchema, "inputSchema");
            Objects.requireNonNull(handler, "handler");
        }
    }

    private final String serverName;
    private final String serverVersion;
    private final Map<String, Tool> tools;
    /** In-flight {@code tools/call} requests by JSON-RPC id (as serialized, so {@code 1} and {@code "1"} stay distinct). */
    private final ConcurrentHashMap<String, CancellationTokenSource> inFlight = new ConcurrentHashMap<>();

    private FoundgineMcpServer(String name, String version, List<Tool> tools) {
        this.serverName = name;
        this.serverVersion = version;
        var map = new LinkedHashMap<String, Tool>();
        for (var t : tools)
            if (map.put(t.name(), t) != null) throw new IllegalArgumentException("Duplicate MCP tool '" + t.name() + "'.");
        this.tools = java.util.Collections.unmodifiableMap(map);
    }

    public static Builder builder() { return new Builder(); }

    public static final class Builder {
        private String name = "foundgine-mcp";
        private String version = "1.0.0";
        private final List<Tool> tools = new ArrayList<>();

        public Builder serverInfo(String name, String version) { this.name = name; this.version = version; return this; }
        public Builder addTool(Tool tool) { tools.add(tool); return this; }

        /** Registers foundgine_capabilities, foundgine_query and foundgine_query_semantic (read-only). */
        public Builder withReadTools(FoundgineMcpTools t) {
            Objects.requireNonNull(t, "tools");
            addTool(new Tool(FoundgineMcpToolNames.CAPABILITIES,
                    "Discover the Foundgine semantic capability contract available to the current caller. Discovery is descriptive; "
                            + "authorization is re-evaluated during execution.",
                    schema(), true, (args, ct) -> CompletableFuture.completedFuture(t.describeCapabilities())));
            addTool(new Tool(FoundgineMcpToolNames.QUERY,
                    "Execute an open Foundgine intent using natural-language lexical grounding. Prefer this tool when the caller has a "
                            + "user-level or natural-language request; Foundgine grounds it against the semantic contract before "
                            + "authorization, planning, and execution. If the intent is already structurally resolved, use "
                            + "foundgine_query_semantic instead. Never supply tenant, identity, authorization predicates, SQL, provider or "
                            + "connection details.",
                    schema("intentJson", "An open intent: either a natural-language expression (for example, 'show customer orders') or "
                            + "a JSON read intent containing rootEntity, selections, and optional filter/order/limit/offset/after. "
                            + "Authentication and authorization context are host-owned."),
                    true, (args, ct) -> t.executeQueryAsync(text(args, "intentJson"), ct)));
            addTool(new Tool(FoundgineMcpToolNames.QUERY_SEMANTIC,
                    "Execute an already-structured provider-neutral Foundgine read intent. Use this when the caller already knows the "
                            + "semantic rootEntity, selections, and optional filter/order/pagination. Use foundgine_query for "
                            + "natural-language intent and lexical grounding. Never supply tenant, identity, authorization predicates, SQL, "
                            + "provider or connection details.",
                    schema("intentJson", "A provider-neutral JSON read intent containing rootEntity, selections, and optional "
                            + "filter/order/limit/offset/after. The semantic model is public; physical storage details are not."),
                    true, (args, ct) -> t.executeSemanticQueryAsync(text(args, "intentJson"), ct)));
            return this;
        }

        /** Registers the mutation tools. Leave them out for agents that must only read. */
        public Builder withMutationTools(FoundgineMcpMutationTools t) {
            Objects.requireNonNull(t, "tools");
            addTool(new Tool(FoundgineMcpToolNames.MUTATION_DRY_RUN,
                    "Validate and authorize a Foundgine mutation without executing it. Returns the exact semantic plan fingerprint and "
                            + "declared effects.",
                    schema("mutationJson", "The mutation JSON (operations)."), false,
                    (args, ct) -> CompletableFuture.completedFuture(t.dryRun(text(args, "mutationJson")))));
            addTool(new Tool(FoundgineMcpToolNames.MUTATION_APPROVE,
                    "Create an approval bound to the exact authorized Foundgine mutation plan. Approval is not authorization and "
                            + "execution re-checks the plan.",
                    schema("mutationJson", "The mutation JSON (operations).", "approvedBy", "Informational only; the approver is always the authenticated caller."),
                    false, (args, ct) -> CompletableFuture.completedFuture(
                            t.approve(text(args, "mutationJson"), optionalText(args, "approvedBy")))));
            addTool(new Tool(FoundgineMcpToolNames.MUTATION_EXECUTE_APPROVED,
                    "Execute only an approved Foundgine mutation after re-authorization and exact plan fingerprint verification. Pass "
                            + "the approval JSON returned by foundgine_mutation_approve.",
                    schema("approvalJson", "The approval JSON returned by foundgine_mutation_approve."), false,
                    (args, ct) -> t.executeApprovedAsync(text(args, "approvalJson"), ct)));
            return this;
        }

        public FoundgineMcpServer build() { return new FoundgineMcpServer(name, version, tools); }
    }

    /**
     * Handles one JSON-RPC message. An empty result means the message needs no reply (notification, client response, or a
     * {@code tools/call} that the client cancelled with {@code notifications/cancelled}: the MCP specification says not to
     * answer a cancelled request; answer HTTP 202 with no body).
     */
    public CompletionStage<Optional<String>> handleAsync(String body) {
        return handleAsync(body, CancellationToken.NONE);
    }

    /**
     * Same as {@link #handleAsync(String)}, and additionally cancels a {@code tools/call} when {@code transportToken} is
     * cancelled (for example because the HTTP client disconnected).
     */
    public CompletionStage<Optional<String>> handleAsync(String body, CancellationToken transportToken) {
        Objects.requireNonNull(transportToken, "transportToken");
        JsonNode request;
        try {
            request = FoundgineMcpJson.READER.readTree(body == null ? "" : body);
        } catch (Exception e) {
            return done(error(null, PARSE_ERROR, "Parse error"));
        }
        if (request == null || !request.isObject())
            return done(error(null, INVALID_REQUEST, "Batches and non-objects are not supported"));
        var id = request.get("id");
        var method = request.path("method").asText("");
        if (method.isEmpty())
            return request.has("result") || request.has("error")
                    ? CompletableFuture.completedFuture(Optional.empty())
                    : done(error(id, INVALID_REQUEST, "method is required"));
        if (id == null || id.isNull()) {
            if ("notifications/cancelled".equals(method)) cancelInFlight(request.path("params"));
            return CompletableFuture.completedFuture(Optional.empty());
        }
        var params = request.path("params");
        return switch (method) {
            case "initialize" -> done(result(id, initialize(params)));
            case "ping" -> done(result(id, FoundgineMcpJson.WRITER.createObjectNode()));
            case "tools/list" -> done(result(id, listTools()));
            case "tools/call" -> callTool(id, params, transportToken);
            default -> done(error(id, METHOD_NOT_FOUND, "Method not found: " + method));
        };
    }

    /** Cancels the in-flight request named by {@code params.requestId}; unknown or finished ids are ignored, as the spec requires. */
    private void cancelInFlight(JsonNode params) {
        var requestId = params.get("requestId");
        if (requestId == null || requestId.isNull()) return;
        var source = inFlight.get(requestId.toString());
        if (source != null) source.cancel();
    }

    /** Result of {@link #invoke}: the tool's text, or an agent-safe error message when {@code isError}. */
    public record ToolResult(boolean isError, String text) {}

    /** The registered tools, in registration order (for adapters to other agent frameworks). */
    public java.util.Collection<Tool> tools() { return tools.values(); }

    /**
     * Runs a tool outside JSON-RPC (used by framework adapters such as LangChain4j) with the same agent-safe error reporting as
     * {@code tools/call}. Blocks until the tool finishes.
     */
    public ToolResult invoke(String name, JsonNode arguments, CancellationToken cancellationToken) {
        var tool = tools.get(name);
        if (tool == null) return new ToolResult(true, "Unknown tool: " + name);
        try {
            return new ToolResult(false, tool.handler().call(arguments, cancellationToken).toCompletableFuture().join());
        } catch (RuntimeException e) {
            return new ToolResult(true, agentSafeMessage(name, e));
        }
    }

    private ObjectNode initialize(JsonNode params) {
        var requested = params.path("protocolVersion").asText("");
        var out = FoundgineMcpJson.WRITER.createObjectNode();
        out.put("protocolVersion", SUPPORTED.contains(requested) ? requested : LATEST_PROTOCOL_VERSION);
        out.putObject("capabilities").putObject("tools").put("listChanged", false);
        out.putObject("serverInfo").put("name", serverName).put("version", serverVersion);
        return out;
    }

    private ObjectNode listTools() {
        var out = FoundgineMcpJson.WRITER.createObjectNode();
        var array = out.putArray("tools");
        for (var t : tools.values()) {
            var n = array.addObject();
            n.put("name", t.name());
            if (t.description() != null) n.put("description", t.description());
            n.set("inputSchema", t.inputSchema());
            n.putObject("annotations").put("readOnlyHint", t.readOnly()).put("openWorldHint", false);
        }
        return out;
    }

    private CompletionStage<Optional<String>> callTool(JsonNode id, JsonNode params, CancellationToken transportToken) {
        var name = params.path("name").asText("");
        var tool = tools.get(name);
        if (tool == null) return done(error(id, INVALID_PARAMS, "Unknown tool: " + name));
        var key = id.toString();
        var source = CancellationTokenSource.createLinkedTokenSource(transportToken);
        inFlight.put(key, source);
        CompletionStage<String> stage;
        try {
            stage = tool.handler().call(params.path("arguments"), source.token());
        } catch (RuntimeException e) {
            stage = CompletableFuture.failedFuture(e);
        }
        return stage.handle((text, failure) -> {
            inFlight.remove(key, source);
            var cancelled = source.isCancellationRequested();
            source.close();
            // A cancelled request gets no response (MCP cancellation utility).
            if (cancelled) return Optional.<String>empty();
            var out = FoundgineMcpJson.WRITER.createObjectNode();
            out.put("isError", failure != null);
            out.putArray("content").addObject().put("type", "text")
                    .put("text", failure == null ? text : agentSafeMessage(name, failure));
            return Optional.of(result(id, out).toString());
        });
    }

    /** Only messages written for agents are shown; internal failures stay in the host's logs. */
    static String agentSafeMessage(String tool, Throwable failure) {
        Throwable t = failure;
        while ((t instanceof CompletionException || t instanceof ExecutionException) && t.getCause() != null) t = t.getCause();
        if (t instanceof SecurityException || t instanceof IllegalArgumentException || t instanceof FoundgineMcpNotConfiguredException)
            return t.getMessage();
        return "An error occurred invoking '" + tool + "'.";
    }

    private static CompletionStage<Optional<String>> done(ObjectNode node) {
        return CompletableFuture.completedFuture(Optional.of(node.toString()));
    }

    private static ObjectNode result(JsonNode id, JsonNode result) {
        var out = envelope(id);
        out.set("result", result);
        return out;
    }

    private static ObjectNode error(JsonNode id, int code, String message) {
        var out = envelope(id);
        out.putObject("error").put("code", code).put("message", message);
        return out;
    }

    private static ObjectNode envelope(JsonNode id) {
        var out = FoundgineMcpJson.WRITER.createObjectNode();
        out.put("jsonrpc", "2.0");
        if (id == null) out.putNull("id"); else out.set("id", id);
        return out;
    }

    private static String text(JsonNode args, String name) {
        var v = args == null ? null : args.get(name);
        if (v == null || !v.isTextual()) throw new IllegalArgumentException("'" + name + "' must be a string.");
        return v.asText();
    }

    private static String optionalText(JsonNode args, String name) {
        var v = args == null ? null : args.get(name);
        return v != null && v.isTextual() ? v.asText() : null;
    }

    /** Object schema with required string properties given as (name, description) pairs. */
    private static ObjectNode schema(String... nameAndDescription) {
        var s = FoundgineMcpJson.WRITER.createObjectNode().put("type", "object");
        var props = s.putObject("properties");
        var required = s.putArray("required");
        for (int i = 0; i + 1 < nameAndDescription.length; i += 2) {
            props.putObject(nameAndDescription[i]).put("type", "string").put("description", nameAndDescription[i + 1]);
            required.add(nameAndDescription[i]);
        }
        return s;
    }
}
