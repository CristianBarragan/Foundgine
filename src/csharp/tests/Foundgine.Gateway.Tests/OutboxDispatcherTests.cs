using Foundgine.Providers.Gateway;
using Foundgine.Providers.Outbox;
using Xunit;

namespace Foundgine.Gateway.Tests;

public sealed class OutboxDispatcherTests
{
    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStore(params FoundgineOutboxLease[] leases) : IFoundgineGatewayOutboxStore
    {
        public List<Guid> Completed { get; } = [];
        public List<(Guid Id, DateTimeOffset? RetryAt, string Error)> Failed { get; } = [];

        public ValueTask<IReadOnlyList<FoundgineOutboxLease>> LeaseAsync(
            int batchSize, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<FoundgineOutboxLease>>(leases);

        public ValueTask<bool> CompleteAsync(FoundgineOutboxLease lease, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            Completed.Add(lease.Message.Id);
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> FailAsync(FoundgineOutboxLease lease, string error, DateTimeOffset? retryAt, DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            Failed.Add((lease.Message.Id, retryAt, error));
            return ValueTask.FromResult(true);
        }
    }

    private sealed class FakePublisher(Func<FoundgineOutboxMessage, bool> succeeds) : IFoundgineOutboxPublisher
    {
        public ValueTask PublishAsync(FoundgineOutboxMessage message, CancellationToken cancellationToken = default) =>
            succeeds(message) ? ValueTask.CompletedTask : throw new InvalidOperationException("broker down");
    }

    private sealed class CapturingEvents : IFoundgineGatewayEventPublisher
    {
        public List<(string Subject, IReadOnlyDictionary<string, string>? Attributes)> Published { get; } = [];

        public ValueTask PublishAsync(FoundgineGatewayEventType type, string subject, string? correlationId = null,
            string? providerId = null, IReadOnlyDictionary<string, string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            Published.Add((subject, attributes));
            return ValueTask.CompletedTask;
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private static FoundgineOutboxLease Lease(int attempt, string payload = "{}") =>
        new(new FoundgineOutboxMessage(Guid.NewGuid(), "t", "orders", payload), attempt, Guid.NewGuid());

    private static FoundgineOutboxDispatcher Create(
        FakeStore store, Func<FoundgineOutboxMessage, bool> succeeds, CapturingEvents? events = null) =>
        new(store, new FakePublisher(succeeds), new FoundgineOutboxDispatcherOptions { MaxAttempts = 3 },
            events, new ManualTime(Now));

    [Fact]
    public async Task Success_CompletesTheLease()
    {
        var lease = Lease(1);
        var store = new FakeStore(lease);
        Assert.Equal(1, await Create(store, _ => true).DispatchOnceAsync());
        Assert.Equal([lease.Message.Id], store.Completed);
        Assert.Empty(store.Failed);
    }

    [Fact]
    public async Task Failure_BelowMaxAttempts_SchedulesRetryWithBackoff()
    {
        var lease = Lease(2);
        var store = new FakeStore(lease);
        await Create(store, _ => false).DispatchOnceAsync();

        var failed = Assert.Single(store.Failed);
        Assert.Equal(Now + TimeSpan.FromSeconds(4), failed.RetryAt); // base 2s * 2^(2-1)
        Assert.Contains("broker down", failed.Error);
        Assert.Empty(store.Completed);
    }

    [Fact]
    public async Task Failure_AtMaxAttempts_DeadLettersAndAnnouncesWithoutPayload()
    {
        var lease = Lease(3, "{\"secret\":\"x\"}");
        var store = new FakeStore(lease);
        var events = new CapturingEvents();
        await Create(store, _ => false, events).DispatchOnceAsync();

        Assert.Null(Assert.Single(store.Failed).RetryAt);
        var announced = Assert.Single(events.Published);
        Assert.Equal("outbox.dead_lettered", announced.Subject);
        Assert.DoesNotContain(announced.Attributes!.Values, v => v.Contains("secret"));
    }

    [Fact]
    public async Task OneFailingMessage_DoesNotBlockTheRestOfTheBatch()
    {
        var bad = Lease(1, "{\"bad\":true}");
        var good = Lease(1);
        var store = new FakeStore(bad, good);
        await Create(store, m => m.Id != bad.Message.Id).DispatchOnceAsync();

        Assert.Equal([good.Message.Id], store.Completed);
        Assert.Equal(bad.Message.Id, Assert.Single(store.Failed).Id);
    }

    [Fact]
    public void Backoff_DoublesAndCapsAtMaximum()
    {
        var options = new FoundgineOutboxDispatcherOptions
        {
            BaseBackoff = TimeSpan.FromSeconds(1), MaxBackoff = TimeSpan.FromSeconds(10)
        };
        Assert.Equal(TimeSpan.FromSeconds(1), FoundgineOutboxDispatcher.ComputeBackoff(1, options));
        Assert.Equal(TimeSpan.FromSeconds(4), FoundgineOutboxDispatcher.ComputeBackoff(3, options));
        Assert.Equal(TimeSpan.FromSeconds(10), FoundgineOutboxDispatcher.ComputeBackoff(50, options));
    }
}
