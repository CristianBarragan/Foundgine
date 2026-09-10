package com.foundgine.runtime;

import com.foundgine.core.execution.*;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.authorization.AllowAllSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.authorization.ISemanticAuthorizationPolicy;
import com.foundgine.core.semantic.security.SecurityInvariantContractValidator;
import java.util.Objects;

/**
 * Framework-neutral Java counterpart of the C# {@code AddFoundgine}
 * extension methods.
 *
 * <p>The C# API targets {@code IServiceCollection}. Java deliberately does
 * not introduce a dependency on one particular DI framework. This adapter
 * performs the same startup trust-boundary work and registers the resulting
 * objects in {@link FoundgineServiceRegistry}. Applications can then bridge
 * that registry into their DI container.
 */
public final class FoundgineServiceCollectionExtensions {
    private FoundgineServiceCollectionExtensions() {}

    public static FoundgineServiceRegistry addFoundgine(
            FoundgineServiceRegistry services,
            SemanticModel model,
            IProviderPlanCompiler compiler,
            IExecutionProvider provider) {
        return addFoundgine(services, model, new AllowAllSemanticAuthorizationPolicy(), compiler, provider);
    }

    public static FoundgineServiceRegistry addFoundgine(
            FoundgineServiceRegistry services,
            SemanticModel model,
            ISemanticAuthorizationPolicy authorizationPolicy,
            IProviderPlanCompiler compiler,
            IExecutionProvider provider) {
        Objects.requireNonNull(services, "services");
        Objects.requireNonNull(model, "model");
        Objects.requireNonNull(authorizationPolicy, "authorizationPolicy");
        Objects.requireNonNull(compiler, "compiler");
        Objects.requireNonNull(provider, "provider");

        FoundgineOptions options = new FoundgineOptions()
                .model(model)
                .authorizationPolicy(authorizationPolicy);

        SemanticContractSnapshot contract = model.freeze().createSnapshot();
        SecurityInvariantContractValidator.ensureContractValid(
                com.foundgine.core.semantic.capabilities.SemanticCapabilityContractDiscovery
                        .describe(model, authorizationPolicy));

        services.addSingletonInstance(FoundgineOptions.class, options);
        services.addSingletonInstance(SemanticContractSnapshot.class, contract);
        services.addSingleton(FoundgineEngine.class,
                () -> new FoundgineEngine(options, contract, compiler, provider));
        services.addSingleton(IFoundgine.class, () -> services.getRequiredService(FoundgineEngine.class));
        services.addSingleton(IFoundgineExecutor.class, () -> services.getRequiredService(FoundgineEngine.class));

        if (options.mutationSchema() != null && options.mutationProvider() != null) {
            services.addSingleton(IFoundgineMutations.class, () -> new FoundgineMutationEngine(
                    options.mutationSchema(),
                    options.authorizationPolicy(),
                    options.mutationProvider(),
                    options.model(),
                    options.warrantKeyResolver(),
                    options.expectedWarrantIssuer(),
                    options.warrantReplayStore(),
                    options.securityResourceLimits()));
        }
        return services;
    }

    public static FoundgineServiceRegistry addFoundgine(
            FoundgineServiceRegistry services,
            FoundgineOptions options,
            IProviderPlanCompiler compiler,
            IExecutionProvider provider) {
        Objects.requireNonNull(options, "options");
        if (options.model() == null)
            throw new IllegalStateException("Foundgine requires FoundgineOptions.model().");
        if (options.authorizationPolicy() == null) {
            if (options.authorizationConfiguration() != null)
                options.authorizationPolicy(new com.foundgine.core.semantic.authorization.ConfiguredSemanticAuthorizationPolicy(
                        options.authorizationConfiguration(), options.authorizationContext()));
            else
                throw new IllegalStateException(
                        "Foundgine requires an ISemanticAuthorizationPolicy or configured authorization.");
        }
        Objects.requireNonNull(services, "services");
        Objects.requireNonNull(compiler, "compiler");
        Objects.requireNonNull(provider, "provider");

        SemanticContractSnapshot contract = options.model().freeze().createSnapshot();
        services.addSingletonInstance(FoundgineOptions.class, options);
        services.addSingletonInstance(SemanticContractSnapshot.class, contract);
        services.addSingleton(FoundgineEngine.class,
                () -> new FoundgineEngine(options, contract, compiler, provider));
        services.addSingleton(IFoundgine.class, () -> services.getRequiredService(FoundgineEngine.class));
        services.addSingleton(IFoundgineExecutor.class, () -> services.getRequiredService(FoundgineEngine.class));
        if (options.mutationSchema() != null && options.mutationProvider() != null) {
            services.addSingleton(IFoundgineMutations.class, () -> new FoundgineMutationEngine(
                    options.mutationSchema(), options.authorizationPolicy(), options.mutationProvider(),
                    options.model(), options.warrantKeyResolver(), options.expectedWarrantIssuer(),
                    options.warrantReplayStore(), options.securityResourceLimits()));
        }
        options.applyCapabilities(services);
        return services;
    }
}
