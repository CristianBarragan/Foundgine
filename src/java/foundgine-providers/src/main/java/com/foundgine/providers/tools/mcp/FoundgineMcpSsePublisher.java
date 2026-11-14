package com.foundgine.providers.tools.mcp;

import com.fasterxml.jackson.databind.JsonNode;
import java.util.Objects;
import java.util.Optional;

/**
 * How trusted host code sends a server-to-client JSON-RPC message (notification, server request, or a response) down a
 * session's SSE stream. The message is validated, re-serialised to one compact line and appended to the session's replay
 * buffer; an open stream is nudged and delivers it, and a client that is not connected receives it on resume.
 *
 * <p>Only sessions that already have a buffer receive anything: the transport creates the buffer when the session is
 * issued, and removes it when the session ends, so publishing to an unknown, ended or evicted session is a no-op
 * ({@link Optional#empty()}) and can never allocate memory for an arbitrary id. The publisher does not authenticate; the
 * caller is host code that already knows which session it means.
 */
public final class FoundgineMcpSsePublisher {
    private final FoundgineMcpSseRegistry registry;

    public FoundgineMcpSsePublisher(FoundgineMcpSseRegistry registry) {
        this.registry = Objects.requireNonNull(registry, "registry");
    }

    /**
     * @return the appended event, or empty when the session has no stream
     * @throws IllegalArgumentException when {@code message} is not a single JSON-RPC 2.0 object (batches are refused) or is
     *         larger than the buffer's byte limit
     */
    public Optional<FoundgineMcpSseBuffer.Event> publish(String sessionId, String message) {
        String compact = validate(message);
        var buffer = registry.find(sessionId);
        return buffer == null ? Optional.empty() : Optional.of(buffer.append(compact));
    }

    private static String validate(String message) {
        Objects.requireNonNull(message, "message");
        JsonNode node;
        try {
            node = FoundgineMcpJson.READER.readTree(message);
        } catch (Exception e) {
            throw new IllegalArgumentException("The message is not valid JSON.");
        }
        if (node == null || !node.isObject()) throw new IllegalArgumentException("The message must be a single JSON-RPC object.");
        if (!"2.0".equals(node.path("jsonrpc").asText(null))) throw new IllegalArgumentException("The message is not JSON-RPC 2.0.");
        boolean call = node.path("method").isTextual();
        boolean response = node.has("id") && (node.has("result") || node.has("error"));
        if (!call && !response) throw new IllegalArgumentException("The message is neither a call nor a response.");
        try {
            return FoundgineMcpJson.WRITER.writeValueAsString(node);
        } catch (Exception e) {
            throw new IllegalArgumentException("The message cannot be serialised.");
        }
    }
}
