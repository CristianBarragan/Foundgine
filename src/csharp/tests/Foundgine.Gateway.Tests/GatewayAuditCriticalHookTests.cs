using Foundgine.Providers.Gateway;
using Foundgine.Providers.Gateway.Audit;
using Foundgine.Providers.Gateway.Events;
using Xunit;

namespace Foundgine.Gateway.Tests;

public sealed class GatewayAuditCriticalHookTests
{
    private sealed class FailingSink : IFoundgineGatewayAuditSink
    {
        public ValueTask AppendAsync(FoundgineGatewayEvent gatewayEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("audit unavailable");
    }

    [Fact]
    public void AuditHook_IsRecognisedByTheHubAsCritical() =>
        Assert.IsAssignableFrom<IFoundgineGatewayCriticalHook>(new FoundgineGatewayAuditHook(new FailingSink()));

    [Fact]
    public async Task AuditFailure_BlocksPublication_AndNothingIsAppended()
    {
        var store = new InMemoryFoundgineGatewayEventStore(100);
        var hub = new FoundgineGatewayEventHub(
            new FoundgineGatewayOptions(),
            new IFoundgineGatewayHook[] { new FoundgineGatewayAuditHook(new FailingSink()) },
            store);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await hub.PublishAsync(FoundgineGatewayEventType.Custom, "tenant.test"));
        Assert.Equal(0L, await store.GetLatestSequenceAsync());
    }
}
