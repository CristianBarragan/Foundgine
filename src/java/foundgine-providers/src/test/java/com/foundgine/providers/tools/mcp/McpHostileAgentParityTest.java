package com.foundgine.providers.tools.mcp;

import static com.foundgine.providers.tools.mcp.McpAdapterParityTest.Engine;
import static com.foundgine.providers.tools.mcp.McpAdapterParityTest.MutEngine;
import static com.foundgine.providers.tools.mcp.McpAdapterParityTest.failure;
import static com.foundgine.providers.tools.mcp.McpAdapterParityTest.host;
import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.serialization.JsonReadIntentAdapter;
import com.foundgine.core.serialization.JsonReadIntentAdapterOptions;
import org.junit.jupiter.api.Test;

/** Port of the C# McpMaliciousIntentSecurityTests / McpSecurityExecutionContextProviderTests hostile-agent corpus. */
class McpHostileAgentParityTest {
    private static final String[] FORBIDDEN = {
        "tenantId", "subject", "audience", "warrant", "authorization", "capabilityId", "provider", "connectionString", "sql"
    };

    private static String withProperty(String name) {
        return "{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"" + name + "\":\"attacker-controlled\"}";
    }

    @Test
    void agentCannotManufactureSecurityOrProviderAuthority() throws Exception {
        for (var property : FORBIDDEN) {
            var direct = assertThrows(IllegalStateException.class, () -> new JsonReadIntentAdapter().parse(withProperty(property)));
            assertTrue(direct.getMessage().toLowerCase().contains(property.toLowerCase()), property);

            var e = new Engine();
            var tools = FoundgineMcpTools.builder(e.proxy).securityContextFactory(() -> host("host-subject")).build();
            // Both agent-facing query tools reject it as a validation error naming the property, and never reach the engine.
            for (var stage : new java.util.concurrent.CompletionStage<?>[] {
                tools.executeQueryAsync(withProperty(property)), tools.executeSemanticQueryAsync(withProperty(property))}) {
                var t = failure(stage);
                assertInstanceOf(IllegalArgumentException.class, t);
                assertTrue(t.getMessage().toLowerCase().contains(property.toLowerCase()), property);
            }
            assertTrue(e.intents.isEmpty(), property);
        }
    }

    @Test
    void hostileFilterValueCannotReplaceHostTenant() throws Exception {
        var json = "{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"filter\":{\"kind\":\"field\","
                + "\"field\":\"TenantId\",\"operator\":\"Eq\",\"value\":\"victim-tenant\"}}";
        assertNull(new JsonReadIntentAdapter().parse(json).security());

        var e = new Engine();
        var ctx = host("host-subject");
        var tools = FoundgineMcpTools.builder(e.proxy).securityContextFactory(() -> ctx).build();
        tools.executeSemanticQueryAsync(json).toCompletableFuture().get();
        assertSame(ctx, e.intents.get(0).security());
    }

    @Test
    void maliciousPredicateStructureIsBoundedBeforePlanning() throws Exception {
        var json = "{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"filter\":{\"kind\":\"or\",\"expressions\":["
                + "{\"kind\":\"or\",\"expressions\":[{\"kind\":\"or\",\"expressions\":[{\"kind\":\"or\",\"expressions\":["
                + "{\"kind\":\"field\",\"field\":\"Id\",\"operator\":\"Eq\",\"value\":1}]}]}]}]}}";
        var bounded = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions(32, 256, 4, 8, 16, true));
        var direct = assertThrows(IllegalStateException.class, () -> bounded.parse(json));
        assertTrue(direct.getMessage().toLowerCase().contains("filter"));

        var e = new Engine();
        var tools = FoundgineMcpTools.builder(e.proxy).adapter(bounded).securityContextFactory(() -> host("a")).build();
        var t = failure(tools.executeSemanticQueryAsync(json));
        assertInstanceOf(IllegalArgumentException.class, t);
        assertTrue(t.getMessage().toLowerCase().contains("filter"));
        assertTrue(e.intents.isEmpty());
    }

    @Test
    void hostContextIsTheOnlyAuthorityEvenWithAnAttackerTenantInTheIntent() throws Exception {
        var e = new Engine();
        var tools = FoundgineMcpTools.builder(e.proxy).securityContextFactory(() -> host("host-subject")).build();
        failure(tools.executeQueryAsync(withProperty("tenantId")));
        assertTrue(e.intents.isEmpty());
    }

    @Test
    void securityProviderIsAcceptedDirectlyAndDefaultsToNoContext() throws Exception {
        var e = new Engine();
        var ctx = host("alice");
        var direct = FoundgineMcpTools.builder(e.proxy).securityContextProvider(() -> ctx).build();
        direct.executeSemanticQueryAsync("{\"rootEntity\":\"Order\",\"selections\":[{\"field\":\"id\"}]}").toCompletableFuture().get();
        assertSame(ctx, e.intents.get(0).security());

        var none = FoundgineMcpTools.builder(e.proxy).build();
        assertThrows(SecurityException.class, none::describeCapabilities);
        var empty = FoundgineMcpTools.builder(e.proxy).securityContextProvider(() -> null).build();
        assertThrows(SecurityException.class, empty::describeCapabilities);
    }

    @Test
    void mutationToolsAcceptProviderOrFactoryButNotBoth() {
        var m = new MutEngine();
        var ctx = host("alice");
        var mutation = "{\"operations\":[{\"entity\":1,\"kind\":\"create\",\"fields\":{\"2\":\"x\"}}]}";
        new FoundgineMcpMutationTools(m.proxy, null, () -> ctx).dryRun(mutation);
        new FoundgineMcpMutationTools(m.proxy, () -> ctx, null).dryRun(mutation);
        assertSame(ctx, m.requests.get(0).security());
        assertSame(ctx, m.requests.get(1).security());
        assertThrows(IllegalArgumentException.class, () -> new FoundgineMcpMutationTools(m.proxy, () -> ctx, () -> ctx));
    }

    @Test
    void discoveryPassesExactlyTheHostContext() {
        var e = new Engine();
        var ctx = host("host-subject");
        var tools = FoundgineMcpTools.builder(e.proxy).securityContextFactory(() -> ctx).build();
        tools.describeCapabilities();
        assertEquals(1, e.discovery.size());
        assertSame(ctx, e.discovery.get(0));
    }
}
