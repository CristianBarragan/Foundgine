package com.foundgine.runtime;

import com.foundgine.core.abstractions.MutationSchema;
import com.foundgine.core.execution.*;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.authorization.*;
import com.foundgine.core.semantic.metadata.IMetadataCatalog;
import com.foundgine.core.semantic.security.execution.SecurityResourceLimits;
import com.foundgine.core.semantic.security.warrants.*;
import com.foundgine.core.semantic.security.warrants.ISecurityWarrantReplayStore;

import java.util.*;
import java.util.function.BiFunction;
import java.util.function.Consumer;

/**
 * Application-level Foundgine configuration.
 *
 * <p>This is the Java counterpart of the C# {@code FoundgineOptions}. The
 * hosting layer is intentionally framework-neutral; use {@link
 * FoundgineServiceRegistry} to adapt registrations to Spring, CDI, Micronaut,
 * or another application container.
 */
public final class FoundgineOptions {
    private IMetadataCatalog metadata;
    private Consumer<com.foundgine.core.semantic.SemanticModelBuilder> semanticConfiguration;
    private SemanticAuthorizationConfiguration authorizationConfiguration;
    private SemanticAuthorizationContext authorizationContext = new SemanticAuthorizationContext();
    private SemanticModel model;
    private ISemanticAuthorizationPolicy authorizationPolicy;
    private IProviderPlanCache planCache;
    private ISecurityWarrantKeyResolver warrantKeyResolver;
    private String expectedWarrantIssuer;
    private ISecurityWarrantReplayStore warrantReplayStore;
    private SecurityResourceLimits securityResourceLimits = new SecurityResourceLimits();
    private IExecutionAuthorizationRevalidator executionAuthorizationRevalidator;
    private BiFunction<com.foundgine.core.semantic.authorization.SemanticAuthorizationEvidence,
            CancellationToken, ExecutionAuthorizationAuthorityState> executionAuthorizationAuthorityResolver;
    private MutationSchema mutationSchema;
    private com.foundgine.core.execution.mutation.IMutationBatchExecutionProvider mutationProvider;

    private final LinkedHashMap<Class<?>, IFoundgineCapability> enabledCapabilities = new LinkedHashMap<>();

    public IMetadataCatalog metadata() { return metadata; }
    public Consumer<com.foundgine.core.semantic.SemanticModelBuilder> semanticConfiguration() { return semanticConfiguration; }
    public SemanticAuthorizationConfiguration authorizationConfiguration() { return authorizationConfiguration; }
    public SemanticAuthorizationContext authorizationContext() { return authorizationContext; }
    public SemanticModel model() { return model; }
    public ISemanticAuthorizationPolicy authorizationPolicy() { return authorizationPolicy; }
    public IProviderPlanCache planCache() { return planCache; }
    public ISecurityWarrantKeyResolver warrantKeyResolver() { return warrantKeyResolver; }
    public String expectedWarrantIssuer() { return expectedWarrantIssuer; }
    public ISecurityWarrantReplayStore warrantReplayStore() { return warrantReplayStore; }
    public SecurityResourceLimits securityResourceLimits() { return securityResourceLimits; }
    public IExecutionAuthorizationRevalidator executionAuthorizationRevalidator() { return executionAuthorizationRevalidator; }
    public BiFunction<SemanticAuthorizationEvidence, CancellationToken, ExecutionAuthorizationAuthorityState>
            executionAuthorizationAuthorityResolver() { return executionAuthorizationAuthorityResolver; }
    public MutationSchema mutationSchema() { return mutationSchema; }
    public com.foundgine.core.execution.mutation.IMutationBatchExecutionProvider mutationProvider() { return mutationProvider; }

    public FoundgineOptions useMetadata(IMetadataCatalog metadata) {
        this.metadata = Objects.requireNonNull(metadata, "metadata");
        return this;
    }

    public FoundgineOptions configureSemantics(Consumer<com.foundgine.core.semantic.SemanticModelBuilder> configure) {
        Objects.requireNonNull(configure, "configure");
        if (semanticConfiguration == null) semanticConfiguration = configure;
        else {
            Consumer<com.foundgine.core.semantic.SemanticModelBuilder> first = semanticConfiguration;
            semanticConfiguration = b -> { first.accept(b); configure.accept(b); };
        }
        return this;
    }

    public FoundgineOptions configureAuthorization(
            Consumer<SemanticAuthorizationConfiguration> configure,
            SemanticAuthorizationContext context) {
        Objects.requireNonNull(configure, "configure");
        if (authorizationConfiguration == null) authorizationConfiguration = new SemanticAuthorizationConfiguration();
        configure.accept(authorizationConfiguration);
        if (context != null) authorizationContext = context;
        return this;
    }

    public FoundgineOptions configureAuthorization(Consumer<SemanticAuthorizationConfiguration> configure) {
        return configureAuthorization(configure, null);
    }

    public FoundgineOptions model(SemanticModel model) { this.model = Objects.requireNonNull(model); return this; }
    public FoundgineOptions authorizationPolicy(ISemanticAuthorizationPolicy policy) {
        this.authorizationPolicy = Objects.requireNonNull(policy); return this;
    }
    public FoundgineOptions planCache(IProviderPlanCache cache) { this.planCache = cache; return this; }
    public FoundgineOptions warrantKeyResolver(ISecurityWarrantKeyResolver resolver) { this.warrantKeyResolver = resolver; return this; }
    public FoundgineOptions expectedWarrantIssuer(String issuer) { this.expectedWarrantIssuer = issuer; return this; }
    public FoundgineOptions warrantReplayStore(ISecurityWarrantReplayStore store) { this.warrantReplayStore = store; return this; }
    public FoundgineOptions securityResourceLimits(SecurityResourceLimits limits) {
        this.securityResourceLimits = Objects.requireNonNull(limits); return this;
    }
    public FoundgineOptions executionAuthorizationRevalidator(IExecutionAuthorizationRevalidator value) {
        this.executionAuthorizationRevalidator = value; return this;
    }
    public FoundgineOptions executionAuthorizationAuthorityResolver(
            BiFunction<SemanticAuthorizationEvidence, CancellationToken, ExecutionAuthorizationAuthorityState> value) {
        this.executionAuthorizationAuthorityResolver = value; return this;
    }
    public FoundgineOptions mutationSchema(MutationSchema schema) { this.mutationSchema = schema; return this; }
    public FoundgineOptions mutationProvider(com.foundgine.core.execution.mutation.IMutationBatchExecutionProvider provider) {
        this.mutationProvider = provider; return this;
    }

    public <T extends IFoundgineCapability> FoundgineOptions enable(Class<T> type, T capability) {
        Objects.requireNonNull(type, "type");
        Objects.requireNonNull(capability, "capability");
        enabledCapabilities.remove(type);
        enabledCapabilities.put(type, capability);
        return this;
    }

    public FoundgineOptions enable(IFoundgineCapability capability) {
        Objects.requireNonNull(capability, "capability");
        Class<?> type = capability.getClass();
        enabledCapabilities.remove(type);
        enabledCapabilities.put(type, capability);
        return this;
    }

    public <T extends IFoundgineCapability> FoundgineOptions disable(Class<T> type) {
        enabledCapabilities.remove(type);
        return this;
    }

    public <T extends IFoundgineCapability> boolean isEnabled(Class<T> type) {
        return enabledCapabilities.containsKey(type);
    }

    public List<Class<?>> enabledCapabilities() {
        return List.copyOf(enabledCapabilities.keySet());
    }

    public void applyCapabilities(FoundgineServiceRegistry services) {
        FoundgineCapabilityContext context = new FoundgineCapabilityContext(this, services);
        for (IFoundgineCapability capability : enabledCapabilities.values())
            capability.configure(context);
    }
}
