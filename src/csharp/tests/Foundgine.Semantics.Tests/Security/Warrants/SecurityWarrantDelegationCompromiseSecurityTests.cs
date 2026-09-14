using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Core.Semantic.Tests.Security.Warrants;

public sealed class SecurityWarrantDelegationCompromiseSecurityTests
{
    [Fact]
    public void Compromising_an_intermediate_node_invalidates_only_its_descendants()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var childA = Child(root, "child-a", "agent-a");
        var childB = Child(root, "child-b", "agent-b");
        var grandchildA = Child(childA, "grandchild-a", "agent-c");
        var store = new MemorySecurityWarrantDelegationCompromiseStore();

        store.Compromise(childA, now, compromisedIssuer: childA.Issuer);

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationCompromiseGuard.Validate(childA, store, now));
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationCompromiseGuard.Validate(grandchildA, store, now));
        SecurityWarrantDelegationCompromiseGuard.Validate(childB, store, now);
    }

    [Fact]
    public void Compromising_root_invalidates_the_entire_delegation_subtree()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var child = Child(root, "child", "agent-b");
        var grandchild = Child(child, "grandchild", "agent-c");
        var store = new MemorySecurityWarrantDelegationCompromiseStore();

        store.Compromise(root, now, compromisedKeyId: root.KeyId);

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationCompromiseGuard.Validate(root, store, now));
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationCompromiseGuard.Validate(child, store, now));
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationCompromiseGuard.Validate(grandchild, store, now));
    }

    [Fact]
    public void Unrelated_sibling_branch_survives_intermediate_compromise()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var childA = Child(root, "child-a", "agent-a");
        var childB = Child(root, "child-b", "agent-b");
        var grandchildB = Child(childB, "grandchild-b", "agent-c");
        var store = new MemorySecurityWarrantDelegationCompromiseStore();

        store.Compromise(childA, now, compromisedKeyId: childA.KeyId);

        SecurityWarrantDelegationCompromiseGuard.Validate(childB, store, now);
        SecurityWarrantDelegationCompromiseGuard.Validate(grandchildB, store, now);
    }

    [Fact]
    public void Compromised_key_is_path_bound_and_does_not_revoke_unrelated_key()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var childA = Child(root, "child-a", "agent-a") with { KeyId = "key-a" };
        var childB = Child(root, "child-b", "agent-b") with { KeyId = "key-b" };
        var store = new MemorySecurityWarrantDelegationCompromiseStore();

        store.Compromise(childA, now, compromisedKeyId: childA.KeyId);

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationCompromiseGuard.Validate(childA, store, now));
        SecurityWarrantDelegationCompromiseGuard.Validate(childB, store, now);
    }

    [Fact]
    public void Compromise_state_change_is_detected_at_final_execution_gate()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var child = Child(root, "child", "agent-b");
        var store = new MemorySecurityWarrantDelegationCompromiseStore();
        SecurityWarrantDelegationCompromiseGuard.Validate(child, store, now);
        var snapshot = SecurityWarrantDelegationCompromiseGuard.Capture(store);

        store.Compromise(child, now, compromisedIssuer: child.Issuer);

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationCompromiseGuard.AssertUnchanged(store, snapshot));
    }

    [Fact]
    public void Repeated_compromise_is_monotonic()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Create(now);
        var store = new MemorySecurityWarrantDelegationCompromiseStore();
        var first = store.Compromise(root, now, compromisedKeyId: root.KeyId);
        var second = store.Compromise(root, now.AddSeconds(1), compromisedKeyId: root.KeyId);

        Assert.Equal(first, second);
        Assert.True(store.CurrentSequence >= second.Sequence);
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationCompromiseGuard.Validate(root, store, now));
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