using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Security.Tests.Security;

/// <summary>Ensures authority-bearing cache partitions cannot cross security contexts.</summary>
public sealed class SecurityAuthorityPartitionRailsTests
{
    [Fact]
    public void Subject_change_changes_partition()
    {
        var warrant = CreateWarrant();
        var first = new SecurityExecutionContext(warrant, "agent-a", "foundgine", "tenant-a", "customer/1");
        var second = first with { Subject = "agent-b" };

        Assert.NotEqual(first.AuthorityCachePartition, second.AuthorityCachePartition);
    }

    [Fact]
    public void Audience_change_changes_partition()
    {
        var warrant = CreateWarrant();
        var first = new SecurityExecutionContext(warrant, "agent", "audience-a", "tenant-a");
        var second = first with { Audience = "audience-b" };

        Assert.NotEqual(first.AuthorityCachePartition, second.AuthorityCachePartition);
    }

    [Fact]
    public void Tenant_change_changes_partition()
    {
        var warrant = CreateWarrant();
        var first = new SecurityExecutionContext(warrant, "agent", "foundgine", "tenant-a");
        var second = first with { Tenant = "tenant-b" };

        Assert.NotEqual(first.AuthorityCachePartition, second.AuthorityCachePartition);
    }

    [Fact]
    public void Resource_scope_change_changes_partition()
    {
        var warrant = CreateWarrant();
        var first = new SecurityExecutionContext(warrant, "agent", "foundgine", "tenant-a", "customer/1");
        var second = first with { ResourceScope = "customer/2" };

        Assert.NotEqual(first.AuthorityCachePartition, second.AuthorityCachePartition);
    }

    [Fact]
    public void Warrant_digest_change_changes_partition_even_when_other_context_values_match()
    {
        var firstWarrant = CreateWarrant("w1", "n1");
        var secondWarrant = CreateWarrant("w2", "n2");
        var first = new SecurityExecutionContext(firstWarrant, "agent", "foundgine", "tenant-a", "customer/1");
        var second = new SecurityExecutionContext(secondWarrant, "agent", "foundgine", "tenant-a", "customer/1");

        Assert.NotEqual(first.AuthorityCachePartition, second.AuthorityCachePartition);
    }

    [Fact]
    public void Null_and_explicit_context_values_do_not_alias()
    {
        var warrant = CreateWarrant();
        var unrestricted = new SecurityExecutionContext(warrant, "agent", "foundgine");
        var tenantScoped = unrestricted with { Tenant = "tenant-a" };
        var resourceScoped = unrestricted with { ResourceScope = "customer/1" };

        Assert.NotEqual(unrestricted.AuthorityCachePartition, tenantScoped.AuthorityCachePartition);
        Assert.NotEqual(unrestricted.AuthorityCachePartition, resourceScoped.AuthorityCachePartition);
        Assert.NotEqual(tenantScoped.AuthorityCachePartition, resourceScoped.AuthorityCachePartition);
    }

    [Fact]
    public void Delimiter_characters_are_still_part_of_the_partition_identity()
    {
        var warrant = CreateWarrant();
        var first = new SecurityExecutionContext(warrant, "agent|a", "foundgine", "tenant-a", "customer/1");
        var second = new SecurityExecutionContext(warrant, "agent", "a|foundgine", "tenant-a", "customer/1");

        Assert.NotEqual(first.AuthorityCachePartition, second.AuthorityCachePartition);
    }

    private static SecurityWarrant CreateWarrant(string id = "warrant", string nonce = "nonce") =>
        new(
            id,
            "issuer",
            "agent",
            "foundgine",
            [new CapabilityGrant("Customer.read", "read")],
            SecurityWarrantConstraints.Unrestricted,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1),
            nonce,
            "key-1",
            null,
            []);
}