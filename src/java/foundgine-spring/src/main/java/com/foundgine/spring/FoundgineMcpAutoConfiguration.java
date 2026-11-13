package com.foundgine.spring;

import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.providers.tools.mcp.FoundgineMcpMutationTools;
import com.foundgine.providers.tools.mcp.FoundgineMcpApprovalTokenPolicy;
import com.foundgine.providers.tools.mcp.FoundgineMcpServer;
import com.foundgine.providers.tools.mcp.FoundgineMcpTools;
import com.foundgine.runtime.IFoundgine;
import com.foundgine.runtime.IFoundgineMutations;
import java.util.List;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.AutoConfiguration;
import org.springframework.boot.autoconfigure.condition.ConditionalOnBean;
import org.springframework.boot.autoconfigure.condition.ConditionalOnClass;
import org.springframework.boot.autoconfigure.condition.ConditionalOnMissingBean;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

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

    /** Only when Spring MVC is present. */
    @Configuration(proxyBeanMethods = false)
    @ConditionalOnClass(name = "org.springframework.web.bind.annotation.RestController")
    static class WebConfiguration {
        @Bean
        @ConditionalOnMissingBean
        @ConditionalOnBean(FoundgineMcpServer.class)
        FoundgineMcpController foundgineMcpController(
                FoundgineMcpServer server, @Value("${foundgine.mcp.allowed-origins:}") List<String> allowedOrigins) {
            return new FoundgineMcpController(server, allowedOrigins);
        }
    }
}
