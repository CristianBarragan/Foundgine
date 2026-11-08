using Foundgine.Providers.Gateway.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Foundgine.Providers.Gateway.Providers;

/// <summary>Validates declared requirements against declared capabilities at host start, then publishes providers to the registry.</summary>
internal sealed class FoundgineProviderStartupValidator(
    IEnumerable<IFoundgineProvider> providers,
    IEnumerable<FoundgineProviderRequirement> requirements,
    IFoundgineGatewayEventStore eventStore,
    IFoundgineGatewayProviderRegistry registry) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var all = providers.ToList();
        // The configured journal may declare itself without a separate registration.
        if (eventStore is IFoundgineProvider journal &&
            !all.Any(p => string.Equals(p.Id, journal.Id, StringComparison.OrdinalIgnoreCase)))
            all.Add(journal);

        FoundgineProviderNegotiator.Negotiate(all, requirements.ToArray()).ThrowIfUnsatisfied();

        foreach (var provider in all)
            if (!registry.TryGet(provider.Id, out _))
                await registry.RegisterAsync(provider.ToDescriptor(), cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class FoundgineProviderServiceCollectionExtensions
{
    /// <summary>Registers a provider so its capabilities participate in startup negotiation and discovery.</summary>
    public static IServiceCollection AddFoundgineProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IFoundgineProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TProvider>();
        services.AddSingleton<IFoundgineProvider>(sp => sp.GetRequiredService<TProvider>());
        return services;
    }

    /// <summary>Declares that some provider of <paramref name="kind"/> must offer every listed capability.</summary>
    public static IServiceCollection RequireFoundgineCapabilities(
        this IServiceCollection services,
        FoundgineProviderKind kind,
        FoundgineProviderCapability required,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(new FoundgineProviderRequirement(kind, required, reason));
        return services;
    }
}
