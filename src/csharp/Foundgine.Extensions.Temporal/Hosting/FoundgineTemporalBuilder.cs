using Foundgine.Extensions.Temporal.Activities;
using Foundgine.Extensions.Temporal.Configuration;
using Foundgine.Extensions.Temporal.Contracts;
using Foundgine.Extensions.Temporal.Messaging;
using Foundgine.Extensions.Temporal.Security;
using Foundgine.Providers.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Foundgine.Extensions.Temporal.Hosting;

/// <summary>Fluent registration surface returned by AddFoundgineTemporal / AddFoundgineTemporalWorker.</summary>
public sealed class FoundgineTemporalBuilder
{
    internal FoundgineTemporalBuilder(IServiceCollection services, FoundgineTemporalOptions options, FoundgineProcessCatalog catalog)
    {
        Services = services;
        Options = options;
        Catalog = catalog;
    }

    public IServiceCollection Services { get; }
    public FoundgineTemporalOptions Options { get; }
    public FoundgineProcessCatalog Catalog { get; }

    /// <summary>Registers a business operation (scoped per activity attempt). Worker hosts only need this.</summary>
    public FoundgineTemporalBuilder AddOperation<TOperation>() where TOperation : class, IFoundgineTemporalOperation
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IFoundgineTemporalOperation, TOperation>());
        return this;
    }

    /// <summary>Adds a process definition. Hosts that start workflows (API, outbox dispatcher) need this.</summary>
    public FoundgineTemporalBuilder AddProcess(FoundgineProcessDefinition definition)
    {
        Catalog.Add(definition);
        return this;
    }

    /// <summary>Routes an outbox topic to a process (used by <see cref="FoundgineTemporalOutboxPublisher"/>).</summary>
    public FoundgineTemporalBuilder RouteTopic(string topic, string process)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(process);
        Options.TopicRoutes[topic] = process;
        return this;
    }

    /// <summary>Host-owned factory that mints the trusted security context for activities. Required on workers.</summary>
    public FoundgineTemporalBuilder UseSecurityContextFactory<TFactory>()
        where TFactory : class, IFoundgineTemporalSecurityContextFactory
    {
        Services.Replace(ServiceDescriptor.Scoped<IFoundgineTemporalSecurityContextFactory, TFactory>());
        return this;
    }

    /// <summary>
    /// Replaces the host's outbox publisher with the Temporal bridge and runs the Foundgine outbox dispatcher.
    /// Requires an outbox provider that declares Retry and DeadLetter (for example the PostgreSQL outbox).
    /// </summary>
    public FoundgineTemporalBuilder DispatchOutboxToTemporal(Action<FoundgineOutboxDispatcherOptions>? configure = null)
    {
        Services.AddFoundgineOutboxDispatcher<FoundgineTemporalOutboxPublisher>(configure);
        return this;
    }
}
