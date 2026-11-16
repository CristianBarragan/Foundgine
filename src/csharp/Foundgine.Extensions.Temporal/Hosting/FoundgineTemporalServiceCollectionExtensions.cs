using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Extensions.Temporal.Activities;
using Foundgine.Extensions.Temporal.Configuration;
using Foundgine.Extensions.Temporal.Security;
using Foundgine.Extensions.Temporal.Workflows;
using Foundgine.Providers.Gateway.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Temporalio.Client;
using Temporalio.Extensions.Hosting;

namespace Foundgine.Extensions.Temporal.Hosting;

public static class FoundgineTemporalServiceCollectionExtensions
{
    /// <summary>
    /// Client side (API, outbox dispatcher): registers a lazily-connected <see cref="ITemporalClient"/>, the workflow
    /// starter and the Temporal provider descriptor. Nothing connects until the first workflow call.
    /// </summary>
    public static FoundgineTemporalBuilder AddFoundgineTemporal(
        this IServiceCollection services, Action<FoundgineTemporalOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(FoundgineTemporalOptions))?.ImplementationInstance;
        if (existing is FoundgineTemporalOptions already)
        {
            configure?.Invoke(already);
            already.Validate();
            return new(services, already, (FoundgineProcessCatalog)services
                .First(d => d.ServiceType == typeof(FoundgineProcessCatalog)).ImplementationInstance!);
        }

        var options = new FoundgineTemporalOptions();
        configure?.Invoke(options);
        options.Validate();
        var catalog = new FoundgineProcessCatalog();

        services.AddSingleton(options);
        services.AddSingleton(catalog);

        services.AddTemporalClient(options.Address, options.Namespace).Configure(client =>
        {
            if (options.UseTls) client.Tls ??= new TlsOptions();
            if (!string.IsNullOrWhiteSpace(options.ApiKey)) client.ApiKey = options.ApiKey;
        });

        services.TryAddSingleton<IFoundgineWorkflowStarter, FoundgineWorkflowStarter>();
        services.AddFoundgineProvider<FoundgineTemporalProvider>();
        return new(services, options, catalog);
    }

    /// <summary>
    /// Worker side: hosts the Temporal worker with the generic process workflow and the operation activity.
    /// Use a dedicated worker process in production. When <paramref name="replaceSecurityContextProvider"/> is true
    /// (default) activities see an activity-scoped <see cref="ISecurityExecutionContextProvider"/>; do not combine that
    /// with an HTTP host that relies on its own provider — keep API and worker in separate processes.
    /// </summary>
    public static FoundgineTemporalBuilder AddFoundgineTemporalWorker(
        this IServiceCollection services,
        Action<FoundgineTemporalOptions>? configure = null,
        bool replaceSecurityContextProvider = true)
    {
        var builder = services.AddFoundgineTemporal(configure);
        var options = builder.Options;

        services.TryAddScoped<FoundgineTemporalSecurityScope>();
        if (replaceSecurityContextProvider)
            services.Replace(ServiceDescriptor.Scoped<ISecurityExecutionContextProvider>(
                sp => sp.GetRequiredService<FoundgineTemporalSecurityScope>()));

        services
            .AddHostedTemporalWorker(options.Address, options.Namespace, options.TaskQueue)
            .ConfigureOptions(worker =>
            {
                if (worker.ClientOptions is null) return;
                if (options.UseTls) worker.ClientOptions.Tls ??= new TlsOptions();
                if (!string.IsNullOrWhiteSpace(options.ApiKey)) worker.ClientOptions.ApiKey = options.ApiKey;
            })
            .AddScopedActivities<FoundgineOperationActivities>()
            .AddWorkflow<FoundgineProcessWorkflow>();

        return builder;
    }
}
