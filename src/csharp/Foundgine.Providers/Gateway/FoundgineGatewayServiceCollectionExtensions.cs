using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Foundgine.Providers.Gateway.Semantics;
using Foundgine.Providers.Gateway.Audit;
using Foundgine.Providers.Gateway.Providers;
using Foundgine.Providers.Gateway.Execution;
using Foundgine.Providers.Gateway.Http;
using Foundgine.Providers.Gateway.Approvals;
using Foundgine.Providers.Gateway.Events;

namespace Foundgine.Providers.Gateway;

/// <summary>Registers the Foundgine gateway facade and its in-process live event stream.</summary>
public static class FoundgineGatewayServiceCollectionExtensions
{
    public static IServiceCollection AddFoundgineGateway(
        this IServiceCollection services,
        Action<FoundgineGatewayOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new FoundgineGatewayOptions();
        configure?.Invoke(options);
        if (options.ReplayPageSize is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(configure), "ReplayPageSize must be 1–10,000.");
        options.EventStream.Validate();
        services.TryAddSingleton(options);
        services.TryAddSingleton(options.EventStream);
        services.TryAddSingleton<FoundgineGatewayScopeDigester>();
        services.TryAddSingleton<IFoundgineGatewayEventStore, InMemoryFoundgineGatewayEventStore>();
        services.TryAddSingleton<FoundgineGatewayEventHub>();
        services.TryAddSingleton<IFoundgineGatewayEventPublisher>(sp =>
            sp.GetRequiredService<FoundgineGatewayEventHub>());
        services.TryAddSingleton<IFoundgineGateway, FoundgineGateway>();
        services.TryAddSingleton<IFoundgineGatewaySemanticSource, FoundgineGatewaySemanticSource>();
        services.TryAddSingleton<IFoundgineGatewayProviderRegistry, FoundgineGatewayProviderRegistry>();
        services.TryAddScoped<IFoundgineGatewayExecution, FoundgineGatewayExecution>();
        services.TryAddScoped<FoundgineGatewayApprovalService>();
        services.TryAddSingleton<FoundgineGatewayHttpOptions>();
        // Fail fast at host start when declared provider capabilities do not satisfy declared requirements.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FoundgineProviderStartupValidator>());
        return services;
    }

    /// <summary>Replaces the default process-local event journal with an application-provided event store.</summary>
    /// <remarks>Call before or after AddFoundgineGateway. Production implementations should provide ordered durable append and replay.</remarks>
    public static IServiceCollection AddFoundgineGatewayEventStore<TStore>(this IServiceCollection services)
        where TStore : class, IFoundgineGatewayEventStore
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TStore>();
        services.Replace(ServiceDescriptor.Singleton<IFoundgineGatewayEventStore>(sp => sp.GetRequiredService<TStore>()));
        return services;
    }

    /// <summary>Registers the single-process approval store for development and single-instance deployments only.</summary>
    /// <remarks>Production multi-instance hosts must register a durable, distributed implementation of <see cref="IFoundgineGatewayApprovalStore"/> instead.</remarks>
    public static IServiceCollection AddFoundgineGatewayInMemoryApprovalStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFoundgineGatewayApprovalStore, InMemoryFoundgineGatewayApprovalStore>();
        return services;
    }

    /// <summary>Registers a durable audit sink as a critical gateway hook.</summary>
    public static IServiceCollection AddFoundgineGatewayAudit<TSink>(this IServiceCollection services)
        where TSink : class, IFoundgineGatewayAuditSink
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TSink>();
        services.TryAddSingleton<IFoundgineGatewayAuditSink>(sp => sp.GetRequiredService<TSink>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFoundgineGatewayHook, FoundgineGatewayAuditHook>());
        return services;
    }
}
