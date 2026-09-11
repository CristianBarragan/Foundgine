using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Core.Semantic.Tests.Security.Warrants;

public sealed class SecurityWarrantDelegationChainSecurityTests
{
    [Fact]
    public void Valid_chain_is_accepted_and_has_stable_chain_digest()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var child = Child(root, "child", "agent-b");
        var grandchild = Child(child, "grandchild", "agent-c");

        SecurityWarrantDelegationChainValidator.Validate([root, child, grandchild], now);
        var first = SecurityWarrantDelegationChainValidator.ChainDigest([root, child, grandchild]);
        var second = SecurityWarrantDelegationChainValidator.ChainDigest([root, child, grandchild]);

        Assert.Equal(first, second);
        Assert.NotEqual(SecurityWarrantDelegationChainValidator.ChainDigest([root, child]), first);
    }

    [Fact]
    public void Reordered_chain_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var child = Child(root, "child", "agent-b");
        var grandchild = Child(child, "grandchild", "agent-c");

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationChainValidator.Validate([root, grandchild, child], now));
    }

    [Fact]
    public void Spliced_ancestor_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var child = Child(root, "child", "agent-b");
        var otherRoot = Create(now) with { Id = "other-root", Subject = "other-agent" };
        var grandchild = Child(child, "grandchild", "agent-c") with
        {
            DelegationPath = [otherRoot.Digest, child.Digest]
        };

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationChainValidator.Validate([root, child, grandchild], now));
    }

    [Fact]
    public void Truncated_chain_is_rejected_when_expected_root_is_a_descendant()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var child = Child(root, "child", "agent-b");
        var grandchild = Child(child, "grandchild", "agent-c");

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationChainValidator.Validate([grandchild], now, root.Digest));
    }

    [Fact]
    public void Forked_child_cannot_be_inserted_into_an_unrelated_chain()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var childA = Child(root, "child-a", "agent-a");
        var childB = Child(root, "child-b", "agent-b");
        var grandchildA = Child(childA, "grandchild-a", "agent-c");

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationChainValidator.Validate([root, childB, grandchildA], now));
    }

    [Fact]
    public void Parent_digest_substitution_is_rejected_even_when_path_looks_correct()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        // Flip the leading digest character deterministically rather than
        // hardcoding a replacement, so the tampered digest is guaranteed to
        // differ from the real one regardless of what it happens to start with.
        var tamperedLeadingChar = root.Digest[0] == '0' ? '1' : '0';
        var child = Child(root, "child", "agent-b") with { ParentDigest = tamperedLeadingChar + root.Digest[1..] };

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationChainValidator.Validate([root, child], now));
    }

    [Fact]
    public void Chain_digest_changes_when_any_ancestor_changes()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var child = Child(root, "child", "agent-b");
        var original = SecurityWarrantDelegationChainValidator.ChainDigest([root, child]);
        var changedRoot = root with { Audience = "different-audience" };
        var changedChild = Child(changedRoot, "child", "agent-b");

        Assert.NotEqual(original, SecurityWarrantDelegationChainValidator.ChainDigest([changedRoot, changedChild]));
    }

    [Fact]
    public void Repeated_warrant_id_or_digest_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var duplicate = root with
        {
            ParentId = root.Id, ParentDigest = root.Digest, Issuer = root.Subject, DelegationPath = [root.Digest]
        };

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationChainValidator.Validate([root, duplicate], now));
    }

    private static SecurityWarrant Child(SecurityWarrant parent, string id, string subject) => parent with
    {
        Id = id,
        Subject = subject,
        Issuer = parent.Subject,
        ParentId = parent.Id,
        ParentDigest = parent.Digest,
        DelegationPath = [.. parent.DelegationPath, parent.Digest],
        IssuedAt = parent.IssuedAt,
        ExpiresAt = parent.ExpiresAt.AddMinutes(-1),
        Signature = []
    };

    private static SecurityWarrant Create(DateTimeOffset now) => new(
        "root", "root-issuer", "agent-a", "foundgine",
        [new CapabilityGrant("Customer.read", "read", ["customer/*"])],
        new SecurityWarrantConstraints(allowedTenants: ["tenant-1"], resourceScopes: ["customer/*"], maxResults: 100),
        now.AddMinutes(-1), now.AddHours(1), "nonce-root", "key-root", null, []);
}