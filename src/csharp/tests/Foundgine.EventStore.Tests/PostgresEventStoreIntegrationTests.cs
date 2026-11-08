
using Foundgine.Providers.EventStore;
using Foundgine.Providers.EventStore.Schema;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Foundgine.EventStore.Tests;

public sealed class PostgresEventStoreIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17.11-alpine") 
        .WithDatabase("foundgine")
        .WithUsername("foundgine")
        .WithPassword("foundgine")
        .Build();

    private NpgsqlDataSource _dataSource = null!;
    private PostgresEventStore _store = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await EventStoreMigrator.ApplyAsync(_dataSource);
        _store = new PostgresEventStore(_dataSource);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Appends_in_order_and_replays_after_new_store_instance()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        Assert.Equal(1, await Append(firstId, "tenant-a", "orders/42", "created"));
        Assert.Equal(2, await Append(secondId, "tenant-a", "orders/42", "paid"));

        var restarted = new PostgresEventStore(_dataSource);
        var replay = await restarted.ReadAsync(
            "tenant-a", "orders/42", 0, null, TestJsonContext.Default.TestPayload);

        Assert.Equal(new[] { firstId, secondId }, replay.Select(x => x.EventId));
        Assert.Equal(new long[] { 1, 2 }, replay.Select(x => x.Sequence));
    }

    [Fact]
    public async Task Duplicate_event_id_is_idempotent()
    {
        var id = Guid.NewGuid();
        var first = await Append(id, "tenant-a", "orders/7", "created");
        var repeated = await Append(id, "tenant-a", "orders/7", "created");
        Assert.Equal(first, repeated);

        var events = await _store.ReadAsync(
            "tenant-a", "orders/7", 0, null, TestJsonContext.Default.TestPayload);
        Assert.Single(events);
    }

    [Fact]
    public async Task Replay_is_tenant_and_stream_isolated()
    {
        await Append(Guid.NewGuid(), "tenant-a", "orders/1", "a");
        await Append(Guid.NewGuid(), "tenant-b", "orders/1", "b");
        await Append(Guid.NewGuid(), "tenant-a", "orders/2", "c");

        var events = await _store.ReadAsync(
            "tenant-a", "orders/1", 0, null, TestJsonContext.Default.TestPayload);
        Assert.Single(events);
        Assert.All(events, e => Assert.Equal("tenant-a", e.TenantId));
    }

    [Fact]
    public async Task Concurrent_appends_allocate_monotonic_sequences()
    {
        var writes = Enumerable.Range(0, 32)
            .Select(_ => Append(Guid.NewGuid(), "tenant-c", "parallel", "item"));
        await Task.WhenAll(writes);

        var events = await _store.ReadAsync(
            "tenant-c", "parallel", 0, 100, TestJsonContext.Default.TestPayload);
        Assert.Equal(32, events.Count);
        Assert.Equal(Enumerable.Range(1, 32).Select(x => (long)x), events.Select(x => x.Sequence));
    }

    [Fact]
    public async Task Retention_deletes_expired_rows_in_bounded_batches()
    {
        await Append(Guid.NewGuid(), "tenant-r", "stream", "old", DateTimeOffset.UtcNow.AddDays(-30));
        await Append(Guid.NewGuid(), "tenant-r", "stream", "new", DateTimeOffset.UtcNow);

        Assert.Equal(1, await _store.DeleteExpiredAsync(DateTimeOffset.UtcNow.AddDays(-7), 1));
        var remaining = await _store.ReadAsync(
            "tenant-r", "stream", 0, 10, TestJsonContext.Default.TestPayload);
        Assert.Single(remaining);
        Assert.Equal("new", remaining[0].Payload.Name);
    }

    private Task<long> Append(
        Guid id, string tenant, string stream, string name, DateTimeOffset? occurredAt = null) =>
        _store.AppendAsync(
            tenant, stream, id, "test.event", occurredAt ?? DateTimeOffset.UtcNow,
            new TestPayload(name, 1), TestJsonContext.Default.TestPayload);
}
