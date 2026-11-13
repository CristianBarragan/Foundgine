package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.CancellationTokenSource;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.atomic.AtomicReference;
import org.junit.jupiter.api.Test;

/** notifications/cancelled and transport-token cancellation reach the in-flight tool call. */
class McpCancellationParityTest {
    private static final ObjectMapper M = new ObjectMapper();

    private static final class Hang {
        final CompletableFuture<String> pending = new CompletableFuture<>();
        final AtomicReference<CancellationToken> seen = new AtomicReference<>();
        final FoundgineMcpServer server = FoundgineMcpServer.builder()
                .addTool(new FoundgineMcpServer.Tool("slow", "slow", M.createObjectNode().put("type", "object"), true,
                        (args, ct) -> {
                            seen.set(ct);
                            ct.register(() -> pending.cancel(false));
                            return pending;
                        }))
                .build();
    }

    private static String call(Object id) {
        return "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":{\"name\":\"slow\",\"arguments\":{}}}";
    }

    @Test
    void cancelledNotificationCancelsTheTokenAndSuppressesTheResponse() throws Exception {
        var h = new Hang();
        var reply = h.server.handleAsync(call(5)).toCompletableFuture();
        assertFalse(reply.isDone());
        assertFalse(h.seen.get().isCancellationRequested());

        var ack = h.server.handleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":5}}")
                .toCompletableFuture().get();
        assertTrue(ack.isEmpty());
        assertTrue(h.seen.get().isCancellationRequested());
        assertTrue(reply.get().isEmpty(), "a cancelled request must not be answered");
    }

    @Test
    void requestIdTypeMustMatch() throws Exception {
        var h = new Hang();
        var reply = h.server.handleAsync(call(5)).toCompletableFuture();
        h.server.handleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":\"5\"}}")
                .toCompletableFuture().get();
        assertFalse(h.seen.get().isCancellationRequested());
        h.pending.complete("ok");
        assertEquals("ok", M.readTree(reply.get().orElseThrow()).at("/result/content/0/text").asText());
    }

    @Test
    void unknownAndFinishedRequestIdsAreIgnored() throws Exception {
        var h = new Hang();
        assertTrue(h.server.handleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":99}}")
                .toCompletableFuture().get().isEmpty());
        h.pending.complete("done");
        h.server.handleAsync(call(1)).toCompletableFuture().get();
        assertTrue(h.server.handleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":1}}")
                .toCompletableFuture().get().isEmpty());
        // malformed notification does not throw
        assertTrue(h.server.handleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\"}")
                .toCompletableFuture().get().isEmpty());
    }

    @Test
    void transportTokenCancellationCancelsTheCall() throws Exception {
        var h = new Hang();
        try (var transport = new CancellationTokenSource()) {
            var reply = h.server.handleAsync(call(7), transport.token()).toCompletableFuture();
            transport.cancel();
            assertTrue(h.seen.get().isCancellationRequested());
            assertTrue(reply.get().isEmpty());
        }
    }

    @Test
    void approveSchemaRequiresApprovedByLikeCSharp() throws Exception {
        var server = FoundgineMcpServer.builder()
                .withMutationTools(new FoundgineMcpMutationTools(null, null, null)).build();
        var list = M.readTree(server.handleAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}")
                .toCompletableFuture().get().orElseThrow());
        for (var t : list.at("/result/tools"))
            if (t.get("name").asText().equals("foundgine_mutation_approve"))
                assertEquals("[\"mutationJson\",\"approvedBy\"]", t.at("/inputSchema/required").toString());
    }
}
