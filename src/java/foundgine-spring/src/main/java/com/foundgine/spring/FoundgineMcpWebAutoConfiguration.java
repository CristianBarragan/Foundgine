package com.foundgine.spring;

import com.foundgine.providers.tools.mcp.FoundgineMcpAuditSink;
import com.foundgine.providers.tools.mcp.FoundgineMcpServer;
import com.foundgine.providers.tools.mcp.FoundgineMcpSessionGate;
import com.foundgine.providers.tools.mcp.FoundgineMcpSseLimits;
import com.foundgine.providers.tools.mcp.FoundgineMcpSseRegistry;
import java.util.List;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.AutoConfiguration;
import org.springframework.boot.autoconfigure.condition.ConditionalOnBean;
import org.springframework.boot.autoconfigure.condition.ConditionalOnClass;
import org.springframework.boot.autoconfigure.condition.ConditionalOnMissingBean;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;

/**
 * The MCP HTTP controller, only when Spring MVC is present.
 *
 * <p>This is a separate auto-configuration, ordered after {@link FoundgineMcpAutoConfiguration}, on purpose. As a nested class of
 * that configuration it was processed <em>before</em> the enclosing class's {@code @Bean} methods, so
 * {@code @ConditionalOnBean(FoundgineMcpServer.class)} never saw the server and the controller was silently never created.
 */
@AutoConfiguration(after = FoundgineMcpAutoConfiguration.class)
@ConditionalOnClass(name = "org.springframework.web.bind.annotation.RestController")
@ConditionalOnProperty(prefix = "foundgine.mcp", name = "enabled", havingValue = "true")
public class FoundgineMcpWebAutoConfiguration {

    @Bean
    @ConditionalOnMissingBean
    @ConditionalOnBean(FoundgineMcpServer.class)
    public FoundgineMcpController foundgineMcpController(
            FoundgineMcpServer server, @Value("${foundgine.mcp.allowed-origins:}") List<String> allowedOrigins,
            ObjectProvider<FoundgineMcpSessionGate> sessionGate, ObjectProvider<FoundgineMcpSseRegistry> sse,
            ObjectProvider<FoundgineMcpAuditSink> audit,
            @Value("${foundgine.mcp.sse-heartbeat-seconds:25}") int heartbeatSeconds,
            @Value("${foundgine.mcp.sse-max-stream-seconds:3600}") int maxStreamSeconds,
            @Value("${foundgine.mcp.sse-sweep-seconds:60}") int sweepSeconds,
            @Value("${foundgine.mcp.sse-max-open-streams:5000}") int maxOpenStreams,
            @Value("${foundgine.mcp.sse-max-streams-per-principal:10}") int maxStreamsPerPrincipal) {
        var options = new FoundgineMcpController.SseOptions(heartbeatSeconds, maxStreamSeconds, sweepSeconds,
                new FoundgineMcpSseLimits(maxOpenStreams, maxStreamsPerPrincipal),
                audit.getIfAvailable(() -> FoundgineMcpAuditSink.NONE));
        return new FoundgineMcpController(server, allowedOrigins, sessionGate.getIfAvailable(), sse.getIfAvailable(), options);
    }
}
