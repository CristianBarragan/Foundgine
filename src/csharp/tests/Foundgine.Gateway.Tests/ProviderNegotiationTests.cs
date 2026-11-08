using Foundgine.Providers.Gateway.Providers;
using Xunit;

namespace Foundgine.Gateway.Tests;

public sealed class ProviderNegotiationTests
{
    private sealed record Stub(string Id, FoundgineProviderKind Kind, FoundgineProviderCapability Capabilities)
        : IFoundgineProvider
    {
        public string Name => Id;
        public string Version => "1.0.0";
    }

    private const FoundgineProviderCapability Durable =
        FoundgineProviderCapability.DurableStorage | FoundgineProviderCapability.TransactionalAppend |
        FoundgineProviderCapability.OrderedEvents | FoundgineProviderCapability.EventReplay;

    [Fact]
    public void Satisfied_WhenOneProviderCoversTheRequirement()
    {
        var result = FoundgineProviderNegotiator.Negotiate(
            [new Stub("pg", FoundgineProviderKind.EventStore, Durable)],
            [new FoundgineProviderRequirement(FoundgineProviderKind.EventStore, Durable)]);
        Assert.True(result.IsSatisfied);
    }

    [Fact]
    public void Fails_AndNamesClosestProvider_WhenCapabilityMissing()
    {
        var memory = new Stub("mem", FoundgineProviderKind.EventStore,
            FoundgineProviderCapability.OrderedEvents | FoundgineProviderCapability.EventReplay);
        var result = FoundgineProviderNegotiator.Negotiate(
            [memory],
            [new FoundgineProviderRequirement(FoundgineProviderKind.EventStore, FoundgineProviderCapability.DurableStorage)]);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("mem", failure.ClosestProviderId);
        Assert.Equal(FoundgineProviderCapability.DurableStorage, failure.Missing);
        Assert.Throws<FoundgineProviderNegotiationException>(result.ThrowIfUnsatisfied);
    }

    [Fact]
    public void Fails_WhenNoProviderOfKindExists()
    {
        var result = FoundgineProviderNegotiator.Negotiate(
            [new Stub("pg", FoundgineProviderKind.EventStore, Durable)],
            [new FoundgineProviderRequirement(FoundgineProviderKind.Workflow, FoundgineProviderCapability.DurableExecution)]);
        Assert.Null(Assert.Single(result.Failures).ClosestProviderId);
    }

    [Fact]
    public void DoesNotMergeCapabilitiesAcrossProviders()
    {
        var result = FoundgineProviderNegotiator.Negotiate(
            [new Stub("a", FoundgineProviderKind.Outbox, FoundgineProviderCapability.Retry),
             new Stub("b", FoundgineProviderKind.Outbox, FoundgineProviderCapability.TransactionalOutbox)],
            [new FoundgineProviderRequirement(FoundgineProviderKind.Outbox,
                FoundgineProviderCapability.Retry | FoundgineProviderCapability.TransactionalOutbox)]);
        Assert.False(result.IsSatisfied);
    }

    [Fact]
    public void DuplicateProviderIds_AreRejected() =>
        Assert.Throws<ArgumentException>(() => FoundgineProviderNegotiator.Negotiate(
            [new Stub("x", FoundgineProviderKind.Outbox, Durable), new Stub("X", FoundgineProviderKind.Outbox, Durable)], []));
}
