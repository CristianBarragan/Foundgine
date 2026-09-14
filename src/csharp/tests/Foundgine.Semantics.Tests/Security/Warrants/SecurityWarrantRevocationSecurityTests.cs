using Xunit;

namespace Foundgine.Core.Semantic.Security.Warrants;

public sealed class SecurityWarrantRevocationSecurityTests
{
    [Fact]
    public void Revoking_parent_invalidates_child_immediately()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create(now);
        var child = Child(parent, "child", "agent-b");
        var store = new MemorySecurityWarrantRevocationStore();

        SecurityWarrantRevocationGuard.Validate(child, store, now);
        store.Revoke(parent, now);

        Assert.Throws<InvalidOperationException>(() => SecurityWarrantRevocationGuard.Validate(child, store, now));
    }

    [Fact]
    public void Revoking_grandparent_invalidates_entire_descendant_chain()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var child = Child(root, "child", "agent-b");
        var grandchild = Child(child, "grandchild", "agent-c");
        var store = new MemorySecurityWarrantRevocationStore();

        store.Revoke(root, now);

        Assert.Throws<InvalidOperationException>(() => SecurityWarrantRevocationGuard.Validate(child, store, now));
        Assert.Throws<InvalidOperationException>(() => SecurityWarrantRevocationGuard.Validate(grandchild, store, now));
    }

    [Fact]
    public void Revoking_child_does_not_revoke_unrelated_new_root()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var child = Child(root, "child", "agent-b");
        var unrelated = Create(now) with { Id = "unrelated", Subject = "agent-x" };
        var store = new MemorySecurityWarrantRevocationStore();

        store.Revoke(child, now);

        Assert.Throws<InvalidOperationException>(() => SecurityWarrantRevocationGuard.Validate(child, store, now));
        SecurityWarrantRevocationGuard.Validate(unrelated, store, now);
    }

    [Fact]
    public void Old_child_cannot_be_resurrected_after_parent_revoke_and_new_grant()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create(now);
        var child = Child(parent, "child", "agent-b");
        var store = new MemorySecurityWarrantRevocationStore();
        store.Revoke(parent, now);

        var newGrant = Create(now) with { Id = "new-grant", Subject = "agent-b" };

        Assert.Throws<InvalidOperationException>(() => SecurityWarrantRevocationGuard.Validate(child, store, now));
        SecurityWarrantRevocationGuard.Validate(newGrant, store, now);
    }

    [Fact]
    public void Forged_delegation_path_cannot_bypass_revocation_when_digest_is_present()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create(now);
        var child = Child(parent, "child", "agent-b");
        var store = new MemorySecurityWarrantRevocationStore();
        store.Revoke(parent, now);

        var forged = child with { DelegationPath = ["unrevoked-parent"] };
        Assert.Throws<InvalidOperationException>(() => SecurityWarrantAttenuator.Attenuate(parent, forged, now));
    }

    [Fact]
    public void Revocation_sequence_is_monotonic()
    {
        var now = DateTimeOffset.UtcNow;
        var a = Create(now);
        var b = Create(now) with { Id = "b" };
        var store = new MemorySecurityWarrantRevocationStore();

        var first = store.Revoke(a, now);
        var second = store.Revoke(b, now);

        Assert.True(second.Sequence > first.Sequence);
        Assert.Equal(2, store.CurrentSequence);
    }

    [Fact]
    public void Execution_snapshot_detects_concurrent_revocation()
    {
        var now = DateTimeOffset.UtcNow;
        var warrant = Create(now);
        var store = new MemorySecurityWarrantRevocationStore();
        var snapshot = SecurityWarrantRevocationGuard.Validate(warrant, store, now);

        store.Revoke(warrant, now);

        Assert.Throws<InvalidOperationException>(() => snapshot.AssertUnchanged(store));
    }

    [Fact]
    public void Revocation_is_idempotent_for_same_warrant_identity_and_digest()
    {
        var now = DateTimeOffset.UtcNow;
        var warrant = Create(now);
        var store = new MemorySecurityWarrantRevocationStore();

        var first = store.Revoke(warrant, now);
        var second = store.Revoke(warrant, now);

        Assert.Equal(first, second);
        Assert.Equal(2, store.CurrentSequence); // attempted transitions remain monotonic
    }

    private static SecurityWarrant Create(DateTimeOffset now) => new(
        "root", "issuer", "agent-a", "foundgine",
        [new CapabilityGrant("Customer.read", "read", ["customer/*"])],
        new SecurityWarrantConstraints(allowedTenants: ["tenant-1"], maxResults: 100),
        now.AddMinutes(-1), now.AddHours(1), "nonce-root", "key-1", null, []);

    private static SecurityWarrant Child(SecurityWarrant parent, string id, string subject) => parent with
    {
        Id = id,
        Subject = subject,
        Issuer = parent.Subject,
        ParentId = parent.Id,
        ParentDigest = parent.Digest,
        DelegationPath = [.. parent.DelegationPath, parent.Digest],
        Signature = [],
        Nonce = "nonce-" + id,
        ExpiresAt = parent.ExpiresAt.AddMinutes(-1)
    };
}