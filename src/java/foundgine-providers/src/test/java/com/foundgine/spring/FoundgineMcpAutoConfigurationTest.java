package com.foundgine.spring;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.providers.tools.mcp.FoundgineMcpServer;
import com.foundgine.runtime.IFoundgine;
import java.lang.reflect.Proxy;
import org.junit.jupiter.api.Test;
import org.springframework.boot.autoconfigure.AutoConfigurations;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;

/** UNVERIFIED: needs the Spring Boot test jars. */
class FoundgineMcpAutoConfigurationTest {
    private final ApplicationContextRunner runner =
            new ApplicationContextRunner().withConfiguration(AutoConfigurations.of(FoundgineMcpAutoConfiguration.class, FoundgineMcpWebAutoConfiguration.class));

    private static IFoundgine foundgine() {
        return (IFoundgine) Proxy.newProxyInstance(IFoundgine.class.getClassLoader(), new Class<?>[] {IFoundgine.class},
                (p, m, a) -> { throw new UnsupportedOperationException(); });
    }

    private static ISecurityExecutionContextProvider identity() {
        return (ISecurityExecutionContextProvider) Proxy.newProxyInstance(
                ISecurityExecutionContextProvider.class.getClassLoader(),
                new Class<?>[] {ISecurityExecutionContextProvider.class}, (p, m, a) -> null);
    }

    @Test
    void offByDefault() {
        runner.withBean(IFoundgine.class, FoundgineMcpAutoConfigurationTest::foundgine)
                .withBean(ISecurityExecutionContextProvider.class, FoundgineMcpAutoConfigurationTest::identity)
                .run(ctx -> assertTrue(ctx.getBeansOfType(FoundgineMcpServer.class).isEmpty()));
    }

    @Test
    void notExposedWithoutAHostIdentityProvider() {
        runner.withPropertyValues("foundgine.mcp.enabled=true")
                .withBean(IFoundgine.class, FoundgineMcpAutoConfigurationTest::foundgine)
                .run(ctx -> assertTrue(ctx.getBeansOfType(FoundgineMcpServer.class).isEmpty()));
    }

    @Test
    void readToolsRegisteredWhenEnabledWithIdentity() {
        runner.withPropertyValues("foundgine.mcp.enabled=true")
                .withBean(IFoundgine.class, FoundgineMcpAutoConfigurationTest::foundgine)
                .withBean(ISecurityExecutionContextProvider.class, FoundgineMcpAutoConfigurationTest::identity)
                .run(ctx -> assertEquals(1, ctx.getBeansOfType(FoundgineMcpServer.class).size()));
    }

    @Test
    void controllerRefusesForeignOriginsAndAnswersOthers() throws Exception {
        var server = FoundgineMcpServer.builder().build();
        var controller = new FoundgineMcpController(server, java.util.List.of("https://app.example"));
        var ping = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}";
        assertEquals(403, controller.post(ping, "https://evil.example", null, null).getStatusCode().value());
        assertEquals(200, controller.post(ping, "https://app.example", null, null).getStatusCode().value());
        assertEquals(200, controller.post(ping, null, null, null).getStatusCode().value());
        assertEquals(202, controller.post("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", null, null, null)
                .getStatusCode().value());
    }

    private static final String INIT = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}";
    private static final String PING = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}";
    private static final String HEADER = com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.HEADER_NAME;

    private static java.security.Principal user(String name) { return () -> name; }

    @Test
    void sessionsAreIssuedRequiredBoundAndTerminated() {
        var server = FoundgineMcpServer.builder().build();
        var gate = new com.foundgine.providers.tools.mcp.FoundgineMcpSessionGate(
                new com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager());
        var controller = new FoundgineMcpController(server, java.util.List.of(), gate);

        var init = controller.post(INIT, null, null, user("alice"));
        assertEquals(200, init.getStatusCode().value());
        String id = init.getHeaders().getFirst(HEADER);
        assertNotNull(id);

        assertEquals(400, controller.post(PING, null, null, user("alice")).getStatusCode().value());
        assertEquals(404, controller.post(PING, null, "A".repeat(43), user("alice")).getStatusCode().value());
        assertEquals(404, controller.post(PING, null, id, user("bob")).getStatusCode().value());
        assertEquals(200, controller.post(PING, null, id, user("alice")).getStatusCode().value());

        assertEquals(404, controller.delete(null, id, user("bob")).getStatusCode().value());
        assertEquals(204, controller.delete(null, id, user("alice")).getStatusCode().value());
        assertEquals(404, controller.delete(null, id, user("alice")).getStatusCode().value());
        assertEquals(404, controller.post(PING, null, id, user("alice")).getStatusCode().value());
    }

    @Test
    void originIsCheckedBeforeSessionsAndStatelessEndpointHasNoDelete() {
        var server = FoundgineMcpServer.builder().build();
        var gate = new com.foundgine.providers.tools.mcp.FoundgineMcpSessionGate(
                new com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager());
        var sessioned = new FoundgineMcpController(server, java.util.List.of(), gate);
        assertEquals(403, sessioned.post(INIT, "https://evil.example", null, null).getStatusCode().value());
        assertEquals(403, sessioned.delete("https://evil.example", null, null).getStatusCode().value());
        assertEquals(405, new FoundgineMcpController(server, java.util.List.of()).delete(null, null, null).getStatusCode().value());
    }

    @Test
    void sessionBeansOnlyWhenEnabled() {
        runner.withPropertyValues("foundgine.mcp.enabled=true")
                .withBean(IFoundgine.class, FoundgineMcpAutoConfigurationTest::foundgine)
                .withBean(ISecurityExecutionContextProvider.class, FoundgineMcpAutoConfigurationTest::identity)
                .run(ctx -> assertTrue(ctx.getBeansOfType(com.foundgine.providers.tools.mcp.FoundgineMcpSessionGate.class).isEmpty()));
        runner.withPropertyValues("foundgine.mcp.enabled=true", "foundgine.mcp.sessions-enabled=true")
                .withBean(IFoundgine.class, FoundgineMcpAutoConfigurationTest::foundgine)
                .withBean(ISecurityExecutionContextProvider.class, FoundgineMcpAutoConfigurationTest::identity)
                .run(ctx -> assertEquals(1, ctx.getBeansOfType(com.foundgine.providers.tools.mcp.FoundgineMcpSessionGate.class).size()));
    }

    @Test
    void sseGetIsSessionBoundOneStreamAtATimeAndMapsResumeErrors() {
        var server = FoundgineMcpServer.builder().build();
        var gate = new com.foundgine.providers.tools.mcp.FoundgineMcpSessionGate(
                new com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager());
        var sse = new com.foundgine.providers.tools.mcp.FoundgineMcpSseRegistry(10, 2, 1000);
        var controller = new FoundgineMcpController(server, java.util.List.of(), gate, sse);
        try {
            String id = controller.post(INIT, null, null, user("alice")).getHeaders().getFirst(HEADER);
            assertNotNull(sse.find(id));                       // the buffer exists from the moment the session is issued
            for (String d : new String[] {"a", "b", "c"}) sse.find(id).append(d);   // keeps 2: ids 2 and 3

            assertEquals(400, controller.get(null, null, null, user("alice")).getStatusCode().value());
            assertEquals(404, controller.get(null, id, null, user("bob")).getStatusCode().value());
            assertEquals(403, controller.get("https://evil.example", id, null, user("alice")).getStatusCode().value());
            assertEquals(410, controller.get(null, id, "0", user("alice")).getStatusCode().value());
            assertEquals(400, controller.get(null, id, "99", user("alice")).getStatusCode().value());
            assertEquals(400, controller.get(null, id, "abc", user("alice")).getStatusCode().value());

            var live = controller.get(null, id, "2", user("alice"));
            assertEquals(200, live.getStatusCode().value());
            assertNotNull(live.getBody());
            assertEquals(409, controller.get(null, id, null, user("alice")).getStatusCode().value());   // a stream is already open

            assertEquals(204, controller.delete(null, id, user("alice")).getStatusCode().value());
            assertNull(sse.find(id));
            assertEquals(404, controller.get(null, id, null, user("alice")).getStatusCode().value());
        } finally {
            controller.close();
        }
    }

    @Test
    void getIs405WithoutSseOrSessions() {
        var server = FoundgineMcpServer.builder().build();
        assertEquals(405, new FoundgineMcpController(server, java.util.List.of()).get(null, null, null, null).getStatusCode().value());
        var gate = new com.foundgine.providers.tools.mcp.FoundgineMcpSessionGate(
                new com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager());
        assertEquals(405, new FoundgineMcpController(server, java.util.List.of(), gate).get(null, null, null, null).getStatusCode().value());
        var sseOnly = new FoundgineMcpController(server, java.util.List.of(), null, new com.foundgine.providers.tools.mcp.FoundgineMcpSseRegistry());
        assertEquals(405, sseOnly.get(null, null, null, null).getStatusCode().value());
        assertThrows(IllegalArgumentException.class, () -> new FoundgineMcpController(server, java.util.List.of(), null, null,
                new FoundgineMcpController.SseOptions(0, 0, 60, null, null)));
    }

    @Test
    void sseStreamsArePerPrincipalLimitedAndAudited() {
        var server = FoundgineMcpServer.builder().build();
        var events = new java.util.concurrent.CopyOnWriteArrayList<com.foundgine.providers.tools.mcp.FoundgineMcpAuditEvent>();
        var gate = new com.foundgine.providers.tools.mcp.FoundgineMcpSessionGate(
                new com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager());
        var sse = new com.foundgine.providers.tools.mcp.FoundgineMcpSseRegistry();
        var options = new FoundgineMcpController.SseOptions(25, 3600, 60,
                new com.foundgine.providers.tools.mcp.FoundgineMcpSseLimits(10, 1), events::add);
        var controller = new FoundgineMcpController(server, java.util.List.of(), gate, sse, options);
        try {
            String first = controller.post(INIT, null, null, user("alice")).getHeaders().getFirst(HEADER);
            String second = controller.post(INIT, null, null, user("alice")).getHeaders().getFirst(HEADER);
            assertEquals(200, controller.get(null, first, null, user("alice")).getStatusCode().value());
            assertEquals(429, controller.get(null, second, null, user("alice")).getStatusCode().value());   // alice's one stream is taken
            assertTrue(events.stream().anyMatch(e -> e.type() == com.foundgine.providers.tools.mcp.FoundgineMcpAuditEvent.Type.SSE_OPENED));
            assertTrue(events.stream().anyMatch(e -> e.type() == com.foundgine.providers.tools.mcp.FoundgineMcpAuditEvent.Type.SSE_REJECTED
                    && "status=LIMIT".equals(e.detail())));
        } finally {
            controller.close();
        }
    }

    @Test
    void sweepDropsBuffersOfSessionsThatEnded() {
        var server = FoundgineMcpServer.builder().build();
        var now = new java.util.concurrent.atomic.AtomicLong(1_000);
        var manager = new com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager(
                new com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.Options(
                        java.time.Duration.ofMillis(100), java.time.Duration.ofMillis(100), 10),
                new com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.InMemoryStore(), now::get);
        var gate = new com.foundgine.providers.tools.mcp.FoundgineMcpSessionGate(manager, com.foundgine.providers.tools.mcp.FoundgineMcpAuditSink.NONE, now::get);
        var sse = new com.foundgine.providers.tools.mcp.FoundgineMcpSseRegistry();
        var controller = new FoundgineMcpController(server, java.util.List.of(), gate, sse);
        try {
            String id = controller.post(INIT, null, null, user("alice")).getHeaders().getFirst(HEADER);
            assertNotNull(sse.find(id));
            controller.sweep();
            assertNotNull(sse.find(id));      // still live
            now.addAndGet(500);
            controller.sweep();
            assertNull(sse.find(id));         // expired: buffer dropped
        } finally {
            controller.close();
        }
    }

    @Test
    void sseRegistryAndPublisherOnlyWhenEnabledWithSessions() {
        var registry = com.foundgine.providers.tools.mcp.FoundgineMcpSseRegistry.class;
        var publisher = com.foundgine.providers.tools.mcp.FoundgineMcpSsePublisher.class;
        var base = runner.withBean(IFoundgine.class, FoundgineMcpAutoConfigurationTest::foundgine)
                .withBean(ISecurityExecutionContextProvider.class, FoundgineMcpAutoConfigurationTest::identity);
        // on: sessions and SSE enabled
        base.withPropertyValues("foundgine.mcp.enabled=true", "foundgine.mcp.sessions-enabled=true", "foundgine.mcp.sse-enabled=true")
                .run(ctx -> {
                    assertEquals(1, ctx.getBeansOfType(registry).size());
                    assertEquals(1, ctx.getBeansOfType(publisher).size());
                    assertEquals(1, ctx.getBeansOfType(FoundgineMcpController.class).size());
                });
        // off: sessions enabled, SSE not
        base.withPropertyValues("foundgine.mcp.enabled=true", "foundgine.mcp.sessions-enabled=true")
                .run(ctx -> {
                    assertTrue(ctx.getBeansOfType(registry).isEmpty());
                    assertTrue(ctx.getBeansOfType(publisher).isEmpty());
                });
        // sessions missing: SSE alone registers nothing
        base.withPropertyValues("foundgine.mcp.enabled=true", "foundgine.mcp.sse-enabled=true")
                .run(ctx -> {
                    assertTrue(ctx.getBeansOfType(registry).isEmpty());
                    assertTrue(ctx.getBeansOfType(publisher).isEmpty());
                });
    }

    @Test
    void sseLimitPropertiesAreAccepted() {
        runner.withPropertyValues("foundgine.mcp.enabled=true", "foundgine.mcp.sessions-enabled=true", "foundgine.mcp.sse-enabled=true",
                        "foundgine.mcp.sse-max-open-streams=3", "foundgine.mcp.sse-max-streams-per-principal=2",
                        "foundgine.mcp.sse-max-stream-seconds=0", "foundgine.mcp.sse-sweep-seconds=5")
                .withBean(IFoundgine.class, FoundgineMcpAutoConfigurationTest::foundgine)
                .withBean(ISecurityExecutionContextProvider.class, FoundgineMcpAutoConfigurationTest::identity)
                .run(ctx -> assertNull(ctx.getStartupFailure()));
    }
}
