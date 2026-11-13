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
            new ApplicationContextRunner().withConfiguration(AutoConfigurations.of(FoundgineMcpAutoConfiguration.class));

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
        assertEquals(403, controller.post(ping, "https://evil.example").getStatusCode().value());
        assertEquals(200, controller.post(ping, "https://app.example").getStatusCode().value());
        assertEquals(200, controller.post(ping, null).getStatusCode().value());
        assertEquals(202, controller.post("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", null)
                .getStatusCode().value());
    }
}
