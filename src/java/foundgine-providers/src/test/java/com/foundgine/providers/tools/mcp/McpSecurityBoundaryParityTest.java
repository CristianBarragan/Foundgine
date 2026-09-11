package com.foundgine.providers.tools.mcp;

import org.junit.jupiter.api.Test;
import java.util.concurrent.CompletableFuture;
import static org.junit.jupiter.api.Assertions.*;

class McpSecurityBoundaryParityTest {
    @Test
    void agentPayloadIsPassedAsIntentAndDoesNotBecomeProviderAuthority() throws Exception {
        final String[] received = new String[1];
        var tools = new FoundgineMcpTools(intent -> {
            received[0] = intent;
            return CompletableFuture.completedFuture(java.util.Map.of("accepted", true));
        });
        tools.foundgineQuery("{\"tenantId\":\"hostile-agent-value\"}").toCompletableFuture().get();
        assertEquals("{\"tenantId\":\"hostile-agent-value\"}", received[0]);
    }

    @Test
    void nullExecutorFailsClosed() {
        assertThrows(NullPointerException.class, () -> new FoundgineMcpTools(null));
    }
}
