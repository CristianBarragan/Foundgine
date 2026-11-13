package com.foundgine.langchain4j;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.providers.tools.mcp.FoundgineMcpServer;
import dev.langchain4j.agent.tool.ToolExecutionRequest;
import dev.langchain4j.agent.tool.ToolSpecification;
import dev.langchain4j.service.tool.ToolExecutor;
import dev.langchain4j.model.chat.request.json.JsonObjectSchema;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Exposes the tools of a {@link FoundgineMcpServer} to LangChain4j agents, e.g.
 * {@code AiServices.builder(Assistant.class).chatModel(model).tools(FoundgineLangChain4jTools.toolMap(server)).build()}.
 * The same security boundary applies as over MCP: tenant and identity come from the host's
 * {@code ISecurityExecutionContextProvider}, never from the model's arguments, and mutation tools exist only if the server was
 * built with them. LangChain4j runs tool executors on the calling thread, so thread-bound identity is visible to the provider.
 * Failures come back as text for the model with the same agent-safe messages as MCP. Compiled and tested against the LangChain4j 1.0.0 sources (javac 21, not Maven); written against the
 * LangChain4j 1.x API.
 */
public final class FoundgineLangChain4jTools {
    private static final ObjectMapper MAPPER = new ObjectMapper();

    private FoundgineLangChain4jTools() {}

    public static Map<ToolSpecification, ToolExecutor> toolMap(FoundgineMcpServer server) {
        var map = new LinkedHashMap<ToolSpecification, ToolExecutor>();
        for (var tool : server.tools()) {
            var name = tool.name();
            ToolExecutor executor = (ToolExecutionRequest request, Object memoryId) -> {
                JsonNode args;
                try {
                    var raw = request.arguments();
                    args = MAPPER.readTree(raw == null || raw.isBlank() ? "{}" : raw);
                } catch (Exception e) {
                    return "Tool arguments must be a JSON object.";
                }
                return server.invoke(name, args, CancellationToken.NONE).text();
            };
            map.put(specification(tool), executor);
        }
        return map;
    }

    static ToolSpecification specification(FoundgineMcpServer.Tool tool) {
        var schema = JsonObjectSchema.builder();
        var props = tool.inputSchema().path("properties");
        for (var it = props.fields(); it.hasNext(); ) {
            var p = it.next();
            schema.addStringProperty(p.getKey(), p.getValue().path("description").asText(""));
        }
        var required = new ArrayList<String>();
        tool.inputSchema().path("required").forEach(n -> required.add(n.asText()));
        if (!required.isEmpty()) schema.required(required);
        return ToolSpecification.builder()
                .name(tool.name())
                .description(tool.description())
                .parameters(schema.build())
                .build();
    }
}
