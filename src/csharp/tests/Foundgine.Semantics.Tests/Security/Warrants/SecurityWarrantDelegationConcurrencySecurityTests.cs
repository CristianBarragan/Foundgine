using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Core.Semantic.Tests.Security.Warrants;

public sealed class SecurityWarrantDelegationConcurrencySecurityTests
{
    [Fact]
    public void Concurrent_children_from_same_parent_require_distinct_fresh_parent_sequences()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var store = new MemorySecurityWarrantDelegationConcurrencyStore();
        var snapshot = store.Capture(root);
        var childA = Child(root, "child-a", "agent-a", "nonce-a");
        var childB = Child(root, "child-b", "agent-b", "nonce-b");

        store.CommitChild(root, childA, snapshot);

        Assert.Throws<InvalidOperationException>(() => store.CommitChild(root, childB, snapshot));
        Assert.False(store.IsCommitted(childB));

        var fresh = store.Capture(root);
        store.CommitChild(root, childB, fresh);
        Assert.True(store.IsCommitted(childB));
    }

    [Fact]
    public void Same_child_identity_cannot_be_committed_twice()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var child = Child(root, "child", "agent-b", "nonce-a");
        var store = new MemorySecurityWarrantDelegationConcurrencyStore();

        store.CommitChild(root, child, store.Capture(root));
        Assert.Throws<InvalidOperationException>(() => store.CommitChild(root, child, store.Capture(root)));
    }

    [Fact]
    public void Same_nonce_cannot_fork_two_children_under_one_parent()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var childA = Child(root, "child-a", "agent-a", "same-nonce");
        var childB = Child(root, "child-b", "agent-b", "same-nonce");
        var store = new MemorySecurityWarrantDelegationConcurrencyStore();

        store.CommitChild(root, childA, store.Capture(root));
        Assert.Throws<InvalidOperationException>(() => store.CommitChild(root, childB, store.Capture(root)));
    }

    [Fact]
    public void Snapshot_is_bound_to_exact_parent_digest()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var other = Create(now) with { Id = "other-root" };
        var child = Child(other, "child", "agent-b", "nonce");
        var store = new MemorySecurityWarrantDelegationConcurrencyStore();
        var snapshot = store.Capture(root);

        Assert.Throws<InvalidOperationException>(() => store.CommitChild(other, child, snapshot));
    }

    [Fact]
    public void Failed_stale_writer_does_not_consume_a_child_slot()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var store = new MemorySecurityWarrantDelegationConcurrencyStore();
        var stale = store.Capture(root);
        var winner = Child(root, "winner", "agent-a", "nonce-winner");
        var loser = Child(root, "loser", "agent-b", "nonce-loser");

        store.CommitChild(root, winner, stale);
        Assert.Throws<InvalidOperationException>(() => store.CommitChild(root, loser, stale));
        var fresh = store.Capture(root);
        store.CommitChild(root, loser, fresh);
        Assert.True(store.IsCommitted(loser));
    }

    [Fact]
    public void Committed_child_still_requires_the_existing_delegation_attenuation_rules()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var invalid = Child(root, "invalid", "agent-b", "nonce") with
        {
            Constraints = SecurityWarrantConstraints.Unrestricted
        };
        var store = new MemorySecurityWarrantDelegationConcurrencyStore();

        Assert.Throws<InvalidOperationException>(() => store.CommitChild(root, invalid, store.Capture(root)));
    }

    private static SecurityWarrant Child(SecurityWarrant parent, string id, string subject, string nonce) => parent with
    {
        Id = id,
        Subject = subject,
        Issuer = parent.Subject,
        ParentId = parent.Id,
        ParentDigest = parent.Digest,
        DelegationPath = [.. parent.DelegationPath, parent.Digest],
        IssuedAt = parent.IssuedAt,
        ExpiresAt = parent.ExpiresAt.AddMinutes(-1),
        Nonce = nonce,
        Signature = []
    };

    private static SecurityWarrant Create(DateTimeOffset now) => new(
        "root", "root-issuer", "agent-a", "foundgine",
        [new CapabilityGrant("Customer.read", "read", ["customer/*"])],
        new SecurityWarrantConstraints(allowedTenants: ["tenant-1"], resourceScopes: ["customer/*"], maxResults: 100),
        now.AddMinutes(-1), now.AddHours(1), "nonce-root", "key-root", null, []);
}
