using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Security.Tests.Security;

public sealed class SecurityPlanCachePartitionTests
{
    [Fact]
    public void Different_warrants_produce_different_authority_partitions()
    {
        var first = CreateWarrant("w-cache-1", "nonce-1");
        var second = CreateWarrant("w-cache-2", "nonce-2");

        var firstContext = new SecurityExecutionContext(first, "agent", "mcp", "tenant-a");
        var secondContext = new SecurityExecutionContext(second, "agent", "mcp", "tenant-a");

        Assert.NotEqual(firstContext.AuthorityCachePartition, secondContext.AuthorityCachePartition);
        Assert.Contains(first.Digest, firstContext.AuthorityCachePartition, StringComparison.Ordinal);
        Assert.Contains(second.Digest, secondContext.AuthorityCachePartition, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_warrant_and_context_partition_identically()
    {
        var warrant = CreateWarrant("w-cache", "nonce-cache");
        var first = new SecurityExecutionContext(warrant, "agent", "mcp", "tenant-a", "customer/1");
        var second = new SecurityExecutionContext(warrant, "agent", "mcp", "tenant-a", "customer/1");

        Assert.Equal(first.AuthorityCachePartition, second.AuthorityCachePartition);
    }

    private static SecurityWarrant CreateWarrant(string id, string nonce) =>
        new(
            id,
            "issuer",
            "agent",
            "mcp",
            [new CapabilityGrant("Customer.read", "read")],
            SecurityWarrantConstraints.Unrestricted,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1),
            nonce,
            "key-1",
            null,
            []);
}