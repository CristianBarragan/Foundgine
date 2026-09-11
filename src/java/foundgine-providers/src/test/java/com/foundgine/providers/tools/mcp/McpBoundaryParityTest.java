package com.foundgine.providers.tools.mcp;

import org.junit.jupiter.api.Test;
import java.util.concurrent.CompletableFuture;
import static org.junit.jupiter.api.Assertions.*;

class McpBoundaryParityTest {
    @Test
    void adapterDelegatesIntentWithoutEmbeddingProviderSpecificExecution() throws Exception {
        var tools = new FoundgineMcpTools(intent -> CompletableFuture.completedFuture(
                java.util.Map.of("accepted", true, "intent", intent)));
        var json = tools.foundgineQuery("{\"intent\":\"orders\"}").toCompletableFuture().get();
        assertTrue(json.contains("accepted"));
        assertTrue(json.contains("orders"));
    }

    @Test
    void executorIsRequired() {
        assertThrows(NullPointerException.class, () -> new FoundgineMcpTools(null));
    }
}
