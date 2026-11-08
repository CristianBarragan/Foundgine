using Foundgine.Providers.Gateway;
using Foundgine.Providers.Gateway.Events;
using Xunit;

namespace Foundgine.Gateway.Tests;

public sealed class GatewayEventJournalTests
{
    [Fact]
    public async Task EventStore_AssignsMonotonicSequenceAndReplaysAfterCursor()
    {
        var store = new InMemoryFoundgineGatewayEventStore(100);
        var first = await store.AppendAsync(CreateEvent("one"));
        var second = await store.AppendAsync(CreateEvent("two"));

        Assert.Equal(1L, first.Sequence);
        Assert.Equal(2L, second.Sequence);
        var replay = await store.ReadAfterAsync(first.Sequence, 10);
        Assert.Single(replay);
        Assert.Equal("two", replay[0].Subject);
    }

    [Fact]
    public async Task EventStore_CorrelationLookupReturnsOnlyMatchingExecution()
    {
        var store = new InMemoryFoundgineGatewayEventStore(100);
        await store.AppendAsync(CreateEvent("execution", "exec-a"));
        await store.AppendAsync(CreateEvent("other", "exec-b"));
        await store.AppendAsync(CreateEvent("execution", "exec-a"));

        var result = await store.ReadByCorrelationIdAsync("exec-a", 10);
        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.Equal("exec-a", item.CorrelationId));
    }

    [Fact]
    public async Task EventStore_EnforcesBoundedRetention()
    {
        var store = new InMemoryFoundgineGatewayEventStore(100);
        for (var i = 0; i < 105; i++) await store.AppendAsync(CreateEvent(i.ToString()));

        var replay = await store.ReadAfterAsync(0, 100);
        Assert.Equal(100, replay.Count);
        Assert.Equal(6L, replay[0].Sequence);
        Assert.Equal(105L, replay[^1].Sequence);
    }

    [Fact]
    public async Task EventHub_ReplaysStoredEventsBeforeFollowingLiveEvents()
    {
        var store = new InMemoryFoundgineGatewayEventStore(100);
        await store.AppendAsync(CreateEvent("historical"));
        var hub = new FoundgineGatewayEventHub(new FoundgineGatewayOptions(), eventStore: store);

        using var cancellation = new CancellationTokenSource();
        await using var enumerator = hub.SubscribeAfterAsync(0, cancellation.Token).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("historical", enumerator.Current.Subject);
        cancellation.Cancel();
    }

    [Fact]
    public async Task EventHub_ResetsCursorWhenProcessLocalJournalHasRestarted()
    {
        var store = new InMemoryFoundgineGatewayEventStore(100);
        var hub = new FoundgineGatewayEventHub(new FoundgineGatewayOptions(), eventStore: store);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = hub.SubscribeAfterAsync(500, cancellation.Token).GetAsyncEnumerator();
        var pending = enumerator.MoveNextAsync().AsTask();
        await hub.PublishAsync(FoundgineGatewayEventType.Custom, "after-restart");
        Assert.True(await pending);
        Assert.Equal("after-restart", enumerator.Current.Subject);
        cancellation.Cancel();
    }

    [Fact]
    public async Task EventHub_DeliversNewEventsAfterReplayCursor()
    {
        var store = new InMemoryFoundgineGatewayEventStore(100);
        var hub = new FoundgineGatewayEventHub(new FoundgineGatewayOptions(), eventStore: store);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = hub.SubscribeAfterAsync(0, cancellation.Token).GetAsyncEnumerator();

        var pending = enumerator.MoveNextAsync().AsTask();
        await hub.PublishAsync(FoundgineGatewayEventType.Custom, "live-event");
        Assert.True(await pending);
        Assert.Equal("live-event", enumerator.Current.Subject);
        cancellation.Cancel();
    }

    private static FoundgineGatewayEvent CreateEvent(string subject, string? correlationId = null) =>
        new(0, DateTimeOffset.UtcNow, FoundgineGatewayEventType.Custom, subject,
            correlationId, null, new Dictionary<string, string>());
}
