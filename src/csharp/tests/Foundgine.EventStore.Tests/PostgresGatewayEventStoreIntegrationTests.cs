using Foundgine.Providers.EventStore;
using Foundgine.Providers.EventStore.Schema;
using Foundgine.Providers.Gateway;
using Foundgine.Providers.Gateway.Providers;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Foundgine.EventStore.Tests;

public sealed class PostgresGatewayEventStoreIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("foundgine").WithUsername("foundgine").WithPassword("foundgine")
        .Build();

    private NpgsqlDataSource _dataSource = null!;
    private PostgresEventStore _events = null!;
    private PostgresGatewayEventStore _journal = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await EventStoreMigrator.ApplyAsync(_dataSource);
        // Small page sizes force the adapter's paging path.
        _events = new PostgresEventStore(_dataSource,
            new EventStoreOptions { MaximumReplayBatchSize = 5, DefaultReplayBatchSize = 5 });
        _journal = new PostgresGatewayEventStore(_events, new PostgresGatewayEventStoreOptions { PageSize = 5 });
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Migrations_are_idempotent_and_concurrency_safe()
    {
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => EventStoreMigrator.ApplyAsync(_dataSource)));
        await EventStoreMigrator.ApplyAsync(_dataSource);
    }

    [Fact]
    public async Task Journal_assigns_monotonic_sequences_and_pages_beyond_store_batch_size()
    {
        for (var i = 0; i < 12; i++) await _journal.AppendAsync(Create($"s{i}"));

        Assert.Equal(12L, await _journal.GetLatestSequenceAsync());
        var all = await _journal.ReadAfterAsync(0, 100);
        Assert.Equal(Enumerable.Range(1, 12).Select(x => (long)x), all.Select(x => x.Sequence));
        Assert.Equal("s0", all[0].Subject);

        var tail = await _journal.ReadAfterAsync(10, 100);
        Assert.Equal(new long[] { 11, 12 }, tail.Select(x => x.Sequence));
    }

    [Fact]
    public async Task Journal_append_is_idempotent_by_event_id()
    {
        var evt = Create("once");
        var first = await _journal.AppendAsync(evt);
        var repeated = await _journal.AppendAsync(evt);
        Assert.Equal(first.Sequence, repeated.Sequence);
        Assert.Single(await _journal.ReadAfterAsync(0, 10));
    }

    [Fact]
    public async Task Journal_correlation_lookup_returns_only_matching_events_oldest_first()
    {
        await _journal.AppendAsync(Create("a1", "exec-a"));
        await _journal.AppendAsync(Create("b1", "exec-b"));
        await _journal.AppendAsync(Create("a2", "exec-a"));

        var result = await _journal.ReadByCorrelationIdAsync("exec-a", 10);
        Assert.Equal(new[] { "a1", "a2" }, result.Select(x => x.Subject));
        Assert.All(result, x => Assert.Equal("exec-a", x.CorrelationId));
    }

    [Fact]
    public async Task Journal_round_trips_type_provider_and_attributes()
    {
        var evt = new FoundgineGatewayEvent(
            0, DateTimeOffset.UtcNow, FoundgineGatewayEventType.ProviderHealthChanged, "p1", "c1", "p1",
            new Dictionary<string, string> { ["health"] = "Healthy" });
        await _journal.AppendAsync(evt);
        var read = Assert.Single(await _journal.ReadAfterAsync(0, 10));
        Assert.Equal(FoundgineGatewayEventType.ProviderHealthChanged, read.Type);
        Assert.Equal("Healthy", read.Attributes["health"]);
        Assert.Equal(evt.EventId, read.EventId);
    }

    [Fact]
    public void Providers_declare_honest_capabilities()
    {
        Assert.True(_events.Capabilities.HasFlag(FoundgineProviderCapability.TenantIsolation));
        Assert.True(_journal.Capabilities.HasFlag(FoundgineProviderCapability.DurableStorage));
        Assert.False(_journal.Capabilities.HasFlag(FoundgineProviderCapability.TenantIsolation));
    }

    [Fact]
    public async Task Latest_sequence_survives_retention()
    {
        var old = new FoundgineGatewayEvent(
            0, DateTimeOffset.UtcNow.AddDays(-30), FoundgineGatewayEventType.Custom, "old", null, null,
            new Dictionary<string, string>());
        await _journal.AppendAsync(old);
        Assert.Equal(1, await _events.DeleteExpiredAsync(DateTimeOffset.UtcNow.AddDays(-7)));
        Assert.Equal(1L, await _journal.GetLatestSequenceAsync());
        Assert.Empty(await _journal.ReadAfterAsync(0, 10));
    }

    private static FoundgineGatewayEvent Create(string subject, string? correlationId = null) =>
        new(0, DateTimeOffset.UtcNow, FoundgineGatewayEventType.Custom, subject, correlationId, null,
            new Dictionary<string, string>());
}
