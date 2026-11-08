using Foundgine.Core.Semantic.Mutation;
using Foundgine.Providers.Gateway.Approvals;
using Foundgine.Runtime;
using Xunit;

namespace Foundgine.Gateway.Tests;

public sealed class GatewayApprovalStoreTests
{
    [Fact]
    public async Task Take_IsOneTime_AndBoundToSubjectAndTenant()
    {
        var store = new InMemoryFoundgineGatewayApprovalStore();
        var approval = CreateApproval();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        await store.StoreAsync("digest", approval, "alice", "tenant-a", expiry);

        Assert.Null(await store.TakeAsync("digest", "bob", "tenant-a", DateTimeOffset.UtcNow));
        Assert.Null(await store.TakeAsync("digest", "alice", "tenant-b", DateTimeOffset.UtcNow));
        Assert.NotNull(await store.TakeAsync("digest", "alice", "tenant-a", DateTimeOffset.UtcNow));
        Assert.Null(await store.TakeAsync("digest", "alice", "tenant-a", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task ExpiredApproval_CannotBeTaken()
    {
        var store = new InMemoryFoundgineGatewayApprovalStore();
        await store.StoreAsync("expired", CreateApproval(), "alice", "tenant-a", DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Null(await store.TakeAsync("expired", "alice", "tenant-a", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Take_ReturnsApprovalOnce_ForMatchingIdentity()
    {
        var store = new InMemoryFoundgineGatewayApprovalStore();
        var approval = CreateApproval();
        await store.StoreAsync("digest", approval, "alice", "tenant-a", DateTimeOffset.UtcNow.AddMinutes(5));

        var result = await store.TakeAsync("digest", "alice", "tenant-a", DateTimeOffset.UtcNow);

        Assert.Same(approval, result);
        Assert.Null(await store.TakeAsync("digest", "alice", "tenant-a", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Revoke_RequiresMatchingIdentity()
    {
        var store = new InMemoryFoundgineGatewayApprovalStore();
        await store.StoreAsync("digest", CreateApproval(), "alice", "tenant-a", DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.False(await store.RevokeAsync("digest", "alice", "tenant-b"));
        Assert.True(await store.RevokeAsync("digest", "alice", "tenant-a"));
        Assert.False(await store.RevokeAsync("digest", "alice", "tenant-a"));
    }

    [Fact]
    public async Task ConcurrentTake_AllowsOnlyOneConsumer()
    {
        var store = new InMemoryFoundgineGatewayApprovalStore();
        await store.StoreAsync("digest", CreateApproval(), "alice", null, DateTimeOffset.UtcNow.AddMinutes(5));
        var attempts = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            store.TakeAsync("digest", "alice", null, DateTimeOffset.UtcNow).AsTask()));
        Assert.Single(attempts, x => x is not null);
    }

    private static MutationPlanApproval CreateApproval() => new(
        new SemanticMutationRequest(new SemanticMutationOperationGraph([])),
        "runtime-approval-id", "fingerprint", "alice", DateTimeOffset.UtcNow);
}
