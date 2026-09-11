package com.foundgine.providers.tools.mcp;

import org.junit.jupiter.api.Test;
import java.util.concurrent.CompletableFuture;
import static org.junit.jupiter.api.Assertions.*;

class McpMutationBoundaryParityTest {
    @Test
    void mutationToolIsTransportOnly() throws Exception {
        var tools = new FoundgineMcpMutationTools(json -> CompletableFuture.completedFuture("accepted:" + json));
        assertEquals("accepted:{\"mutation\":\"cancelOrder\"}",
                tools.foundgineMutation("{\"mutation\":\"cancelOrder\"}").toCompletableFuture().get());
    }

    @Test
    void mutationExecutorIsRequired() {
        assertThrows(NullPointerException.class, () -> new FoundgineMcpMutationTools(null));
    }
}
