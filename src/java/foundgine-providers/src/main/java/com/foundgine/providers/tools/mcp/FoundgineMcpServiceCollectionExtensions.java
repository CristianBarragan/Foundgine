package com.foundgine.providers.tools.mcp;

import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.semantic.resolution.SemanticLexicalReadIntentGrounder;
import com.foundgine.core.semantic.security.execution.DelegateSecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.serialization.JsonReadIntentAdapter;
import com.foundgine.runtime.FoundgineServiceRegistry;
import com.foundgine.runtime.IFoundgine;
import com.foundgine.runtime.IFoundgineMutations;
import java.util.Objects;
import java.util.function.Supplier;

/**
 * Registration of the Foundgine MCP adapter (port of {@code AddFoundgineMcp}). The application remains responsible for the MCP
 * transport ({@link FoundgineMcpServer} is a ready core) and for supplying execution context such as tenant and caller identity.
 */
public final class FoundgineMcpServiceCollectionExtensions {
    private FoundgineMcpServiceCollectionExtensions() {}

    /** Legacy: registers an already-built tools instance. */
    public static void register(FoundgineServiceRegistry r, FoundgineMcpTools tools) {
        r.addSingletonInstance(FoundgineMcpTools.class, tools);
    }

    /**
     * Registers {@link JsonReadIntentAdapter}, {@link FoundgineMcpTools} and {@link FoundgineMcpMutationTools} as singletons.
     * {@link IFoundgine} must be registered by the time the tools are first resolved; {@link IFoundgineMutations} and
     * {@link SemanticLexicalReadIntentGrounder} are optional (without them the mutation tools and open lexical intents report
     * "not configured"). A provider already in the registry is used when none is passed.
     *
     * @param contextFactory execution-context values per call (optional)
     * @param securityContextProvider host-owned source of the caller's context; preferred over the factory
     * @param securityContextFactory delegate form of the provider; both may not be supplied together
     */
    public static FoundgineServiceRegistry addFoundgineMcp(
            FoundgineServiceRegistry services,
            Supplier<ExecutionContext> contextFactory,
            ISecurityExecutionContextProvider securityContextProvider,
            Supplier<SecurityExecutionContext> securityContextFactory) {
        return addFoundgineMcp(services, contextFactory, securityContextProvider, securityContextFactory, null, null);
    }

    /** Backwards-compatible registration overload; signed-token execution remains disabled without a policy. */
    public static FoundgineServiceRegistry addFoundgineMcp(
            FoundgineServiceRegistry services, Supplier<ExecutionContext> contextFactory,
            ISecurityExecutionContextProvider securityContextProvider,
            Supplier<SecurityExecutionContext> securityContextFactory,
            FoundgineMcpApprovalToken.Signer approvalTokenSigner,
            FoundgineMcpApprovalToken.KeyResolver approvalTokenKeys) {
        return addFoundgineMcp(services, contextFactory, securityContextProvider, securityContextFactory,
                approvalTokenSigner, approvalTokenKeys, null);
    }

    /** Registers MCP tools with host-owned signing, verification, expiry and replay services. */
    public static FoundgineServiceRegistry addFoundgineMcp(
            FoundgineServiceRegistry services,
            Supplier<ExecutionContext> contextFactory,
            ISecurityExecutionContextProvider securityContextProvider,
            Supplier<SecurityExecutionContext> securityContextFactory,
            FoundgineMcpApprovalToken.Signer approvalTokenSigner,
            FoundgineMcpApprovalToken.KeyResolver approvalTokenKeys,
            FoundgineMcpApprovalTokenPolicy approvalTokenPolicy) {
        Objects.requireNonNull(services, "services");
        if (securityContextProvider != null && securityContextFactory != null)
            throw new IllegalArgumentException(
                    "Only one of securityContextProvider or securityContextFactory may be supplied.");

        services.addSingleton(JsonReadIntentAdapter.class, memo(JsonReadIntentAdapter::new));
        if (securityContextProvider != null)
            services.addSingletonInstance(ISecurityExecutionContextProvider.class, securityContextProvider);
        else if (securityContextFactory != null)
            services.addSingletonInstance(ISecurityExecutionContextProvider.class,
                    new DelegateSecurityExecutionContextProvider(securityContextFactory));

        services.addSingleton(FoundgineMcpTools.class, memo(() -> new FoundgineMcpTools(
                services.getRequiredService(IFoundgine.class),
                services.getRequiredService(JsonReadIntentAdapter.class),
                contextFactory,
                services.getService(ISecurityExecutionContextProvider.class).orElse(null),
                null,
                services.getService(SemanticLexicalReadIntentGrounder.class).orElse(null))));
        services.addSingleton(FoundgineMcpMutationTools.class, memo(() -> new FoundgineMcpMutationTools(
                services.getService(IFoundgineMutations.class).orElse(null),
                null,
                services.getService(ISecurityExecutionContextProvider.class).orElse(null),
                approvalTokenSigner, approvalTokenKeys, approvalTokenPolicy)));
        return services;
    }

    /** {@link FoundgineServiceRegistry#addSingleton} calls its supplier on every lookup, so singletons memoize themselves. */
    private static <T> Supplier<T> memo(Supplier<T> factory) {
        return new Supplier<>() {
            private T value;
            @Override
            public synchronized T get() {
                if (value == null) value = factory.get();
                return value;
            }
        };
    }
}
