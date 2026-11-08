using Foundgine.Providers.Gateway;
using Foundgine.Providers.Gateway.Events;
using Foundgine.Providers.Gateway.Http;
using Foundgine.Providers.Gateway.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Foundgine.Providers.EventStore;

public sealed class PostgresGatewayEventStoreOptions
{
    /// <summary>System tenant owning the gateway journal. Gateway events are not tenant-partitioned.</summary>
    public string TenantId { get; set; } = "foundgine-gateway";
    public string StreamId { get; set; } = "gateway-events";
    /// <summary>Rows per database round trip; must not exceed the event store's maximum replay batch.</summary>
    public int PageSize { get; set; } = 500;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(TenantId)) throw new ArgumentException("TenantId is required.");
        if (string.IsNullOrWhiteSpace(StreamId)) throw new ArgumentException("StreamId is required.");
        if (PageSize < 1) throw new ArgumentOutOfRangeException(nameof(PageSize));
    }
}

/// <summary>
/// Adapts the tenant/stream <see cref="IEventStore"/> to the gateway's global, ordered journal contract.
/// The whole journal is one stream, so its sequence is gap-free and monotonic across instances; appends to it
/// are serialized by the store (correctness over throughput). Idempotent by the event's stable EventId.
/// </summary>
public sealed class PostgresGatewayEventStore : IFoundgineGatewayEventStore, IFoundgineProvider
{
    private readonly IEventStore _store;
    private readonly PostgresGatewayEventStoreOptions _options;

    public PostgresGatewayEventStore(IEventStore store, PostgresGatewayEventStoreOptions options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public string Id => "foundgine.postgres.gateway-journal";
    public string Name => "PostgreSQL gateway event journal";
    public string Version => "1.0.0";
    public FoundgineProviderKind Kind => FoundgineProviderKind.EventStore;

    /// <summary>No <c>TenantIsolation</c>: the gateway journal is a single system-tenant log.</summary>
    public FoundgineProviderCapability Capabilities =>
        FoundgineProviderCapability.DurableStorage |
        FoundgineProviderCapability.OrderedEvents |
        FoundgineProviderCapability.EventReplay |
        FoundgineProviderCapability.TransactionalAppend;

    public async ValueTask<FoundgineGatewayEvent> AppendAsync(
        FoundgineGatewayEvent gatewayEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gatewayEvent);
        var sequence = await _store.AppendAsync(
            _options.TenantId, _options.StreamId, gatewayEvent.EventId, gatewayEvent.Type.ToString(),
            gatewayEvent.OccurredAt, gatewayEvent with { Sequence = 0 },
            FoundgineGatewayJsonContext.Default.FoundgineGatewayEvent,
            gatewayEvent.CorrelationId, cancellationToken).ConfigureAwait(false);
        return gatewayEvent with { Sequence = sequence };
    }

    public async ValueTask<long> GetLatestSequenceAsync(CancellationToken cancellationToken = default) =>
        await _store.GetLatestSequenceAsync(_options.TenantId, _options.StreamId, cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<IReadOnlyList<FoundgineGatewayEvent>> ReadAfterAsync(
        long sequence, int limit, CancellationToken cancellationToken = default)
    {
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (limit is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(limit));

        var result = new List<FoundgineGatewayEvent>(Math.Min(limit, 1024));
        var cursor = sequence;
        while (result.Count < limit)
        {
            var take = Math.Min(_options.PageSize, limit - result.Count);
            var page = await _store.ReadAsync(
                _options.TenantId, _options.StreamId, cursor, take,
                FoundgineGatewayJsonContext.Default.FoundgineGatewayEvent, cancellationToken).ConfigureAwait(false);
            foreach (var envelope in page)
                result.Add(envelope.Payload with { Sequence = envelope.Sequence });
            if (page.Count < take) break;
            cursor = page[^1].Sequence;
        }
        return result;
    }

    /// <remarks>Returns at most <see cref="PostgresGatewayEventStoreOptions.PageSize"/> latest matches.</remarks>
    public async ValueTask<IReadOnlyList<FoundgineGatewayEvent>> ReadByCorrelationIdAsync(
        string correlationId, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (limit is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(limit));
        var page = await _store.ReadByCorrelationAsync(
            _options.TenantId, _options.StreamId, correlationId, Math.Min(limit, _options.PageSize),
            FoundgineGatewayJsonContext.Default.FoundgineGatewayEvent, cancellationToken).ConfigureAwait(false);
        return page.Select(e => e.Payload with { Sequence = e.Sequence }).ToArray();
    }
}

public static class PostgresGatewayEventStoreServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the process-local gateway journal with the PostgreSQL journal. The host must register an
    /// <see cref="NpgsqlDataSource"/> and run <c>EventStoreMigrator.ApplyAsync</c> before serving traffic.
    /// </summary>
    public static IServiceCollection AddFoundgineGatewayPostgresEventStore(
        this IServiceCollection services, Action<PostgresGatewayEventStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new PostgresGatewayEventStoreOptions();
        configure?.Invoke(options);
        options.Validate();
        services.TryAddSingleton(options);
        services.TryAddSingleton(new EventStoreOptions());
        services.TryAddSingleton<IEventStore>(sp => new PostgresEventStore(
            sp.GetRequiredService<NpgsqlDataSource>(), sp.GetRequiredService<EventStoreOptions>()));
        return services.AddFoundgineGatewayEventStore<PostgresGatewayEventStore>();
    }
}
