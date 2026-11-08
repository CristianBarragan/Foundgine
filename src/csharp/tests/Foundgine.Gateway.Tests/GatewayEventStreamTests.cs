using System.Security.Cryptography;
using System.Text;
using Foundgine.Providers.Gateway;
using Foundgine.Providers.Gateway.Events;
using Xunit;

namespace Foundgine.Gateway.Tests;

public sealed class GatewayEventStreamTests
{
    private static FoundgineGatewayEvent Event(
        FoundgineGatewayEventType type = FoundgineGatewayEventType.Custom, string subject = "s",
        params (string Key, string Value)[] attributes) =>
        new(0, DateTimeOffset.UtcNow, type, subject, null, null,
            attributes.ToDictionary(a => a.Key, a => a.Value));

    private static readonly FoundgineGatewayViewer Alice = new("owner-alice", "tenant-a", false);

    // ---- audience rules -------------------------------------------------------------------------------

    [Fact]
    public void OwnerScopedEvent_IsVisibleOnlyToItsOwner()
    {
        var evt = Event(attributes: (FoundgineGatewayEventAttributes.OwnerDigest, "owner-alice"));
        Assert.True(FoundgineGatewayEventVisibility.IsVisible(evt, Alice));
        Assert.False(FoundgineGatewayEventVisibility.IsVisible(evt, Alice with { OwnerDigest = "owner-bob" }));
        // Even an operator of the same tenant does not see another principal's owner-scoped events.
        Assert.False(FoundgineGatewayEventVisibility.IsVisible(evt, new("owner-bob", "tenant-a", true)));
    }

    [Fact]
    public void TenantScopedEvent_IsVisibleToThatTenantOnly()
    {
        var evt = Event(attributes: (FoundgineGatewayEventAttributes.TenantDigest, "tenant-a"));
        Assert.True(FoundgineGatewayEventVisibility.IsVisible(evt, Alice));
        Assert.False(FoundgineGatewayEventVisibility.IsVisible(evt, Alice with { TenantDigest = "tenant-b", IsOperator = true }));
    }

    [Theory]
    [InlineData(FoundgineGatewayEventType.ProviderHealthChanged)]
    [InlineData(FoundgineGatewayEventType.ProviderRegistered)]
    [InlineData(FoundgineGatewayEventType.AuthorizationDenied)]
    [InlineData(FoundgineGatewayEventType.ExecutionStarted)]
    [InlineData(FoundgineGatewayEventType.Custom)]
    public void UnscopedSystemEvents_AreOperatorOnly(FoundgineGatewayEventType type)
    {
        Assert.False(FoundgineGatewayEventVisibility.IsVisible(Event(type), Alice));
        Assert.True(FoundgineGatewayEventVisibility.IsVisible(Event(type), Alice with { IsOperator = true }));
    }

    [Theory]
    [InlineData(FoundgineGatewayEventType.CapabilityChanged)]
    [InlineData(FoundgineGatewayEventType.SemanticModelChanged)]
    public void DiscoveryChanges_AreVisibleToEveryAuthenticatedCaller(FoundgineGatewayEventType type) =>
        Assert.True(FoundgineGatewayEventVisibility.IsVisible(Event(type), Alice));

    [Fact]
    public void ToPublic_StripsReservedAttributesAndKeepsTheRest()
    {
        var evt = Event(attributes: new[]
        {
            (FoundgineGatewayEventAttributes.OwnerDigest, "x"),
            (FoundgineGatewayEventAttributes.TenantDigest, "y"),
            ("operation.kind", "read")
        });
        var publicEvent = FoundgineGatewayEventVisibility.ToPublic(evt);
        Assert.Equal(["operation.kind"], publicEvent.Attributes.Keys);
    }

    // ---- digests ---------------------------------------------------------------------------------------

    [Fact]
    public void UnconfiguredScopeDigestKey_IsNull()
    {
        Assert.False(
            new FoundgineGatewayEventStreamOptions()
                .ScopeDigestKey.HasValue);
    }
    
    [Fact]
    public void UnkeyedOwnerDigest_MatchesTheLegacyFormat()
    {
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("t1\0alice")));
        var digester = new FoundgineGatewayScopeDigester(new FoundgineGatewayEventStreamOptions());
        Assert.Equal(expected, digester.Owner("t1", "alice"));
    }

    [Fact]
    public void KeyedDigests_AreStableKeyDependentAndDomainSeparated()
    {
        var unkeyed = new FoundgineGatewayScopeDigester(new FoundgineGatewayEventStreamOptions());
        var keyA = new FoundgineGatewayEventStreamOptions().SetScopeDigestKey(new byte[32].Select((_, i) => (byte)i).ToArray());
        var keyB = new FoundgineGatewayEventStreamOptions().SetScopeDigestKey(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray());
        var a = new FoundgineGatewayScopeDigester(keyA);
        var b = new FoundgineGatewayScopeDigester(keyB);

        Assert.Equal(a.Owner("t1", "alice"), new FoundgineGatewayScopeDigester(keyA).Owner("t1", "alice"));
        Assert.NotEqual(a.Owner("t1", "alice"), b.Owner("t1", "alice"));
        Assert.NotEqual(a.Owner("t1", "alice"), unkeyed.Owner("t1", "alice"));

        // An owner in a tenant literally named "tenant" must not collide with another tenant's digest.
        foreach (var d in new[] { unkeyed, a })
            Assert.NotEqual(d.Owner("tenant", "X"), d.Tenant("X"));
    }

    [Fact]
    public void ScopeDigestKey_MustBeAtLeast32Bytes() =>
        Assert.Throws<ArgumentException>(() => new FoundgineGatewayEventStreamOptions().SetScopeDigestKey(new byte[31]));

    // ---- cursor policy ---------------------------------------------------------------------------------

    [Fact]
    public void NoCursor_MeansLiveOnlyFromTheHighWaterMark() =>
        Assert.Equal(new FoundgineGatewayCursorResolution(FoundgineGatewayCursorStatus.Live, 500),
            FoundgineGatewayEventCursor.Resolve(null, 500, 100));

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("99999999999999999999")]
    public void MalformedCursor_IsInvalid(string raw) =>
        Assert.Equal(FoundgineGatewayCursorStatus.Invalid, FoundgineGatewayEventCursor.Resolve(raw, 500, 100).Status);

    [Fact]
    public void CursorInsideTheWindow_Replays() =>
        Assert.Equal(new FoundgineGatewayCursorResolution(FoundgineGatewayCursorStatus.Replay, 450),
            FoundgineGatewayEventCursor.Resolve("450", 500, 100));

    [Fact]
    public void ExplicitZero_ReplaysOnlyWhenTheJournalIsWithinTheWindow()
    {
        Assert.Equal(FoundgineGatewayCursorStatus.Replay, FoundgineGatewayEventCursor.Resolve("0", 80, 100).Status);
        Assert.Equal(FoundgineGatewayCursorStatus.TooOld, FoundgineGatewayEventCursor.Resolve("0", 5_000, 100).Status);
    }

    [Fact]
    public void CursorAheadOfAnOldJournal_IsAcceptedBecauseTheHubRestartsAtZero() =>
        Assert.Equal(FoundgineGatewayCursorStatus.Replay, FoundgineGatewayEventCursor.Resolve("900", 3, 100).Status);

    // ---- hub replay and back-fill ----------------------------------------------------------------------

    [Fact]
    public async Task Hub_ReplaysAcrossPagesInOrder()
    {
        var store = new InMemoryFoundgineGatewayEventStore(1_000);
        for (var i = 0; i < 10; i++) await store.AppendAsync(Event(subject: i.ToString()));
        var hub = new FoundgineGatewayEventHub(new FoundgineGatewayOptions { ReplayPageSize = 3 }, eventStore: store);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var enumerator = hub.SubscribeAfterAsync(0, cts.Token).GetAsyncEnumerator(cts.Token);
        var seen = new List<long>();
        while (seen.Count < 10)
        {
            Assert.True(await enumerator.MoveNextAsync());
            seen.Add(enumerator.Current.Sequence);
        }
        Assert.Equal(Enumerable.Range(1, 10).Select(x => (long)x), seen);
    }

    [Fact]
    public async Task Hub_BackfillsEventsDroppedByAFullSubscriberBuffer()
    {
        var store = new InMemoryFoundgineGatewayEventStore(1_000);
        var hub = new FoundgineGatewayEventHub(
            new FoundgineGatewayOptions { SubscriberBufferCapacity = 2 }, eventStore: store);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var enumerator = hub.SubscribeAfterAsync(0, cts.Token).GetAsyncEnumerator(cts.Token);
        var first = enumerator.MoveNextAsync().AsTask(); // registers the subscriber, then waits for live events
        await hub.PublishAsync(FoundgineGatewayEventType.Custom, "1");
        Assert.True(await first);

        // The consumer is idle: a buffer of 2 keeps only the newest events (5 and 6).
        for (var i = 2; i <= 6; i++) await hub.PublishAsync(FoundgineGatewayEventType.Custom, i.ToString());

        var seen = new List<long> { enumerator.Current.Sequence };
        while (seen.Count < 6)
        {
            Assert.True(await enumerator.MoveNextAsync());
            seen.Add(enumerator.Current.Sequence);
        }
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6 }, seen);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_001)]
    public void Hub_RejectsInvalidReplayPageSize(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FoundgineGatewayEventHub(new FoundgineGatewayOptions { ReplayPageSize = size }));
}
