using Foundgine.Providers.EventStore.Schema;
using Foundgine.Providers.Gateway.Providers;
using Foundgine.Providers.Outbox;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Foundgine.EventStore.Tests;

public sealed class PostgresOutboxIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("foundgine").WithUsername("foundgine").WithPassword("foundgine")
        .Build();

    private NpgsqlDataSource _dataSource = null!;
    private PostgresOutbox _outbox = null!;
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await EventStoreMigrator.ApplyAsync(_dataSource);
        _outbox = new PostgresOutbox(_dataSource);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Enqueue_is_idempotent_and_round_trips_fields()
    {
        var message = new FoundgineOutboxMessage(
            Guid.NewGuid(), "tenant-a", "orders.created", "{\"id\":42}", "order-42", "corr-1",
            new Dictionary<string, string> { ["schema"] = "v1" });
        await _outbox.EnqueueAsync(message);
        await _outbox.EnqueueAsync(message);

        var leases = await _outbox.LeaseAsync(10, Lease, DateTimeOffset.UtcNow);
        var lease = Assert.Single(leases);
        Assert.Equal(1, lease.Attempt);
        Assert.Equal("tenant-a", lease.Message.TenantId);
        Assert.Equal("order-42", lease.Message.Key);
        Assert.Equal("corr-1", lease.Message.CorrelationId);
        Assert.Equal("v1", lease.Message.Headers!["schema"]);
        Assert.Contains("42", lease.Message.PayloadJson);
    }

    [Fact]
    public async Task Transactional_enqueue_rolls_back_with_the_caller_and_commits_with_it()
    {
        var rolledBack = Message("rolled-back");
        await using (var connection = await _dataSource.OpenConnectionAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await _outbox.EnqueueAsync(transaction, rolledBack);
            await transaction.RollbackAsync();
        }
        Assert.Empty(await _outbox.LeaseAsync(10, Lease, DateTimeOffset.UtcNow));

        var committed = Message("committed");
        await using (var connection = await _dataSource.OpenConnectionAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await _outbox.EnqueueAsync(transaction, committed);
            // Not visible to dispatchers before the caller commits.
            Assert.Empty(await _outbox.LeaseAsync(10, Lease, DateTimeOffset.UtcNow));
            await transaction.CommitAsync();
        }
        Assert.Equal(committed.Id, Assert.Single(await _outbox.LeaseAsync(10, Lease, DateTimeOffset.UtcNow)).Message.Id);
    }

    [Fact]
    public async Task Concurrent_leases_never_overlap()
    {
        for (var i = 0; i < 20; i++) await _outbox.EnqueueAsync(Message($"m{i}"));
        var now = DateTimeOffset.UtcNow;

        var batches = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => _outbox.LeaseAsync(10, Lease, now).AsTask()));
        var ids = batches.SelectMany(b => b).Select(l => l.Message.Id).ToList();
        Assert.Equal(20, ids.Count);
        Assert.Equal(20, ids.Distinct().Count());
    }

    [Fact]
    public async Task Expired_lease_is_redelivered_with_incremented_attempt_and_old_token_is_rejected()
    {
        await _outbox.EnqueueAsync(Message("slow"));
        var now = DateTimeOffset.UtcNow;
        var first = Assert.Single(await _outbox.LeaseAsync(10, TimeSpan.FromSeconds(1), now));
        Assert.Empty(await _outbox.LeaseAsync(10, Lease, now)); // still leased

        var second = Assert.Single(await _outbox.LeaseAsync(10, Lease, now.AddSeconds(2)));
        Assert.Equal(2, second.Attempt);
        Assert.False(await _outbox.CompleteAsync(first, now.AddSeconds(2)));
        Assert.True(await _outbox.CompleteAsync(second, now.AddSeconds(2)));
        Assert.Empty(await _outbox.LeaseAsync(10, Lease, now.AddSeconds(60)));
    }

    [Fact]
    public async Task Failure_with_retry_time_is_not_due_until_then()
    {
        await _outbox.EnqueueAsync(Message("retry"));
        var now = DateTimeOffset.UtcNow;
        var lease = Assert.Single(await _outbox.LeaseAsync(10, Lease, now));
        Assert.True(await _outbox.FailAsync(lease, "boom", now.AddSeconds(10), now));

        Assert.Empty(await _outbox.LeaseAsync(10, Lease, now.AddSeconds(5)));
        Assert.Equal(2, Assert.Single(await _outbox.LeaseAsync(10, Lease, now.AddSeconds(11))).Attempt);
    }

    [Fact]
    public async Task Dead_lettered_messages_are_never_leased_again()
    {
        await _outbox.EnqueueAsync(Message("dead"));
        var now = DateTimeOffset.UtcNow;
        var lease = Assert.Single(await _outbox.LeaseAsync(10, Lease, now));
        Assert.True(await _outbox.FailAsync(lease, "poison", null, now));
        Assert.Empty(await _outbox.LeaseAsync(10, Lease, now.AddDays(1)));
    }

    [Fact]
    public async Task Retention_removes_only_completed_rows()
    {
        await _outbox.EnqueueAsync(Message("done"));
        await _outbox.EnqueueAsync(Message("dead"));
        var now = DateTimeOffset.UtcNow;
        var leases = await _outbox.LeaseAsync(10, Lease, now);
        await _outbox.CompleteAsync(leases[0], now);
        await _outbox.FailAsync(leases[1], "poison", null, now);

        Assert.Equal(1, await _outbox.DeleteCompletedAsync(now.AddMinutes(1)));
        Assert.Equal(0, await _outbox.DeleteCompletedAsync(now.AddMinutes(1)));
    }

    [Fact]
    public void Declares_the_capabilities_the_dispatcher_requires()
    {
        var required = FoundgineProviderCapability.TransactionalOutbox |
                       FoundgineProviderCapability.Retry | FoundgineProviderCapability.DeadLetter;
        Assert.True(FoundgineProviderNegotiator.Negotiate(
            [_outbox], [new FoundgineProviderRequirement(FoundgineProviderKind.Outbox, required)]).IsSatisfied);
    }

    private static FoundgineOutboxMessage Message(string name) =>
        new(Guid.NewGuid(), "tenant-a", "test.topic", $"{{\"name\":\"{name}\"}}");
}
