using System.Collections.Concurrent;

namespace Foundgine.Providers.Gateway.Providers;

public enum FoundgineGatewayProviderHealth
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy
}

/// <summary>Stable, transport-neutral description of a registered provider.</summary>
public sealed record FoundgineGatewayProviderDescriptor(
    string Id,
    string Name,
    string Version,
    IReadOnlyList<string> Capabilities);

public sealed record FoundgineGatewayProviderStatus(
    FoundgineGatewayProviderDescriptor Provider,
    FoundgineGatewayProviderHealth Health,
    DateTimeOffset UpdatedAt,
    string? Description = null);

/// <summary>Provider lifecycle surface for registration, health reporting and discovery.</summary>
public interface IFoundgineGatewayProviderRegistry
{
    IReadOnlyList<FoundgineGatewayProviderStatus> GetSnapshot();
    bool TryGet(string providerId, out FoundgineGatewayProviderStatus? status);
    ValueTask RegisterAsync(FoundgineGatewayProviderDescriptor provider, CancellationToken cancellationToken = default);
    ValueTask ReportHealthAsync(string providerId, FoundgineGatewayProviderHealth health, string? description = null, CancellationToken cancellationToken = default);
    ValueTask<bool> UnregisterAsync(string providerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thread-safe provider registry. It reports lifecycle changes through the gateway event publisher;
/// it does not instantiate providers or grant them execution authority.
/// </summary>
public sealed class FoundgineGatewayProviderRegistry : IFoundgineGatewayProviderRegistry
{
    private readonly ConcurrentDictionary<string, FoundgineGatewayProviderStatus> _providers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IFoundgineGatewayEventPublisher _events;

    public FoundgineGatewayProviderRegistry(IFoundgineGatewayEventPublisher events) =>
        _events = events ?? throw new ArgumentNullException(nameof(events));

    public IReadOnlyList<FoundgineGatewayProviderStatus> GetSnapshot() =>
        _providers.Values.OrderBy(x => x.Provider.Id, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool TryGet(string providerId, out FoundgineGatewayProviderStatus? status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return _providers.TryGetValue(providerId, out status);
    }

    public async ValueTask RegisterAsync(
        FoundgineGatewayProviderDescriptor provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.Version);
        if (provider.Capabilities is null)
            throw new ArgumentException("Provider capabilities are required.", nameof(provider));

        var status = new FoundgineGatewayProviderStatus(
            provider with { Capabilities = provider.Capabilities.ToArray() },
            FoundgineGatewayProviderHealth.Unknown,
            DateTimeOffset.UtcNow);
        if (!_providers.TryAdd(provider.Id, status))
            throw new InvalidOperationException($"Provider '{provider.Id}' is already registered.");

        try
        {
            await _events.PublishAsync(
                FoundgineGatewayEventType.ProviderRegistered,
                provider.Id,
                providerId: provider.Id,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (_providers.TryGetValue(provider.Id, out var current) && ReferenceEquals(current, status))
                _providers.TryRemove(provider.Id, out _);
            throw;
        }
    }

    public async ValueTask ReportHealthAsync(
        string providerId,
        FoundgineGatewayProviderHealth health,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (!Enum.IsDefined(health)) throw new ArgumentOutOfRangeException(nameof(health));
        FoundgineGatewayProviderStatus previous;
        FoundgineGatewayProviderStatus updated;
        while (true)
        {
            if (!_providers.TryGetValue(providerId, out previous!))
                throw new KeyNotFoundException($"Provider '{providerId}' is not registered.");
            updated = previous with { Health = health, UpdatedAt = DateTimeOffset.UtcNow, Description = description };
            if (_providers.TryUpdate(providerId, updated, previous)) break;
        }

        try
        {
            await _events.PublishAsync(
                FoundgineGatewayEventType.ProviderHealthChanged,
                providerId,
                providerId: providerId,
                attributes: new Dictionary<string, string>
                {
                    ["health"] = health.ToString(),
                    ["description"] = description ?? string.Empty
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _providers.TryUpdate(providerId, previous, updated);
            throw;
        }
    }

    public async ValueTask<bool> UnregisterAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (!_providers.TryRemove(providerId, out var removed)) return false;
        try
        {
            await _events.PublishAsync(
                FoundgineGatewayEventType.ProviderUnregistered,
                providerId,
                providerId: providerId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            _providers.TryAdd(providerId, removed);
            throw;
        }
    }
}
