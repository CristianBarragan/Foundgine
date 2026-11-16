package com.foundgine.spring;

import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.providers.tools.mcp.FoundgineMcpMutationTools;
import com.foundgine.providers.tools.mcp.FoundgineMcpApprovalTokenPolicy;
import com.foundgine.providers.tools.mcp.FoundgineMcpAuditSink;
import com.foundgine.providers.tools.mcp.FoundgineMcpServer;
import com.foundgine.providers.tools.mcp.FoundgineMcpSessionGate;
import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager;
import com.foundgine.providers.tools.mcp.FoundgineMcpSsePublisher;
import com.foundgine.providers.tools.mcp.FoundgineMcpSseRegistry;
import com.foundgine.providers.tools.mcp.FoundgineMcpTools;
import com.foundgine.runtime.IFoundgine;
import com.foundgine.runtime.IFoundgineMutations;
import java.time.Duration;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.AutoConfiguration;
import org.springframework.boot.autoconfigure.condition.ConditionalOnBean;
import org.springframework.boot.autoconfigure.condition.ConditionalOnClass;
import org.springframework.boot.autoconfigure.condition.ConditionalOnMissingBean;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;

/**
 * Exposes Foundgine through MCP. Secure by default, so nothing is registered unless
 * {@code foundgine.mcp.enabled=true} <em>and</em> the application supplies an {@link IFoundgine} and an
 * {@link ISecurityExecutionContextProvider} (the trusted identity of the current request; Foundgine never takes identity
 * from the agent). Mutation tools additionally need {@code foundgine.mcp.mutations-enabled=true} and an
 * {@link IFoundgineMutations} bean.
 *
 * <p>With Spring MVC on the classpath a POST endpoint ({@code foundgine.mcp.path}, default {@code /mcp}) is also
 * registered. Protect it with your own authentication (e.g. Spring Security) so the provider can read the caller.
 * UNVERIFIED: written without the Spring Boot jars.
 */
@AutoConfiguration
@ConditionalOnClass(FoundgineMcpServer.class)
@ConditionalOnProperty(prefix = "foundgine.mcp", name = "enabled", havingValue = "true")
public class FoundgineMcpAutoConfiguration {

    @Bean
    @ConditionalOnMissingBean
    @ConditionalOnBean({IFoundgine.class, ISecurityExecutionContextProvider.class})
    public FoundgineMcpTools foundgineMcpTools(
            IFoundgine foundgine, ISecurityExecutionContextProvider securityContextProvider) {
        return FoundgineMcpTools.builder(foundgine).securityContextProvider(securityContextProvider).build();
    }

    @Bean
    @ConditionalOnMissingBean
    @ConditionalOnBean({IFoundgineMutations.class, ISecurityExecutionContextProvider.class, FoundgineMcpApprovalTokenPolicy.class})
    @ConditionalOnProperty(prefix = "foundgine.mcp", name = "mutations-enabled", havingValue = "true")
    public FoundgineMcpMutationTools foundgineMcpMutationTools(
            IFoundgineMutations mutations, ISecurityExecutionContextProvider securityContextProvider,
            FoundgineMcpApprovalTokenPolicy approvalTokenPolicy) {
        return new FoundgineMcpMutationTools(mutations, null, securityContextProvider, null, null, approvalTokenPolicy);
    }

    @Bean
    @ConditionalOnMissingBean
    @ConditionalOnBean(FoundgineMcpTools.class)
    public FoundgineMcpServer foundgineMcpServer(
            FoundgineMcpTools tools,
            ObjectProvider<FoundgineMcpMutationTools> mutationTools,
            @Value("${foundgine.mcp.server-name:foundgine-mcp}") String name,
            @Value("${foundgine.mcp.server-version:1.0.0}") String version) {
        var builder = FoundgineMcpServer.builder().serverInfo(name, version).withReadTools(tools);
        mutationTools.ifAvailable(builder::withMutationTools);
        return builder.build();
    }

    /**
     * Sessions: {@code foundgine.mcp.sessions-enabled=true}. {@code foundgine.mcp.session-idle-minutes} (default 30),
     * {@code foundgine.mcp.session-max-lifetime-minutes} (default 1440), {@code foundgine.mcp.max-sessions} (default 10000).
     * The default store is in-memory (single instance): declare your own manager bean with a shared store for clusters.
     * With SSE enabled the MCP controller purges expired sessions itself on a timer; without SSE call {@link FoundgineMcpSessionManager#purgeExpired()} periodically. A {@link FoundgineMcpAuditSink} bean, if present, receives session audit events.
     */
    @Bean
    @ConditionalOnMissingBean
    @ConditionalOnBean(FoundgineMcpServer.class)
    @ConditionalOnProperty(prefix = "foundgine.mcp", name = "sessions-enabled", havingValue = "true")
    public FoundgineMcpSessionManager foundgineMcpSessionManager(
            @Value("${foundgine.mcp.session-idle-minutes:30}") long idleMinutes,
            @Value("${foundgine.mcp.session-max-lifetime-minutes:1440}") long maxLifetimeMinutes,
            @Value("${foundgine.mcp.max-sessions:10000}") int maxSessions) {
        return new FoundgineMcpSessionManager(
                new FoundgineMcpSessionManager.Options(Duration.ofMinutes(idleMinutes), Duration.ofMinutes(maxLifetimeMinutes), maxSessions),
                new FoundgineMcpSessionManager.InMemoryStore(), System::currentTimeMillis);
    }

    @Bean
    @ConditionalOnMissingBean
    @ConditionalOnBean(FoundgineMcpSessionManager.class)
    public FoundgineMcpSessionGate foundgineMcpSessionGate(
            FoundgineMcpSessionManager manager, ObjectProvider<FoundgineMcpAuditSink> audit) {
        return new FoundgineMcpSessionGate(manager, audit.getIfAvailable(() -> FoundgineMcpAuditSink.NONE), System::currentTimeMillis);
    }

    /**
     * SSE replay buffers: {@code foundgine.mcp.sse-enabled=true} (needs sessions). {@code foundgine.mcp.sse-max-streams}
     * (default 10000), {@code sse-max-events} (default 256) and {@code sse-max-bytes} (default 1048576) per session.
     */
    @Bean
    @ConditionalOnMissingBean
    @ConditionalOnBean(FoundgineMcpSessionManager.class)
    @ConditionalOnProperty(prefix = "foundgine.mcp", name = "sse-enabled", havingValue = "true")
    public FoundgineMcpSseRegistry foundgineMcpSseRegistry(
            @Value("${foundgine.mcp.sse-max-streams:10000}") int maxStreams,
            @Value("${foundgine.mcp.sse-max-events:256}") int maxEvents,
            @Value("${foundgine.mcp.sse-max-bytes:1048576}") long maxBytes) {
        return new FoundgineMcpSseRegistry(maxStreams, maxEvents, maxBytes);
    }

    /**
     * Inject this to send server-to-client messages (notifications, server requests) down a session's SSE stream:
     * {@code publisher.publish(sessionId, jsonRpcMessage)}. {@code foundgine.mcp.sse-heartbeat-seconds} (default 25) keeps idle
     * connections open. Streams end after {@code sse-max-stream-seconds} (default 3600, 0 = no limit; the client reconnects with
     * {@code Last-Event-ID}); {@code sse-sweep-seconds} (default 60) sets the expiry sweep; {@code sse-max-open-streams} (default
     * 5000) and {@code sse-max-streams-per-principal} (default 10) cap concurrent streams (429 beyond). Open, refused and closed
     * streams go to the {@link FoundgineMcpAuditSink} bean.
     */
    @Bean
    @ConditionalOnMissingBean
    @ConditionalOnBean(FoundgineMcpSseRegistry.class)
    public FoundgineMcpSsePublisher foundgineMcpSsePublisher(FoundgineMcpSseRegistry registry) {
        return new FoundgineMcpSsePublisher(registry);
    }
}
