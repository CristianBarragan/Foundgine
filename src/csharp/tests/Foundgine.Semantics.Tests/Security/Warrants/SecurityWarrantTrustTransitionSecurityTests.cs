using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Core.Semantic.Tests.Security.Warrants;

public sealed class SecurityWarrantTrustTransitionSecurityTests
{
    [Fact]
    public void Trust_change_after_validation_is_rejected_at_final_gate()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new MemorySecurityWarrantDelegationTrustStateStore();
        var parent = Create("p", "root", "service-a");
        var child = Child(parent);
        store.Set(Trust("service-a"));

        var snapshot =
            SecurityWarrantDelegationTrustTransition.ValidateAndCapture(parent, child, store, now, "tenant-a");
        store.Set(Trust("service-a", canDelegate: false));

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationTrustTransition.AssertUnchanged(snapshot, store));
    }

    [Fact]
    public void Key_rotation_after_validation_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new MemorySecurityWarrantDelegationTrustStateStore();
        var parent = Create("p", "root", "service-a");
        var child = Child(parent, keyId: "key-v1");
        store.Set(Trust("service-a", "key-v1"));

        var snapshot =
            SecurityWarrantDelegationTrustTransition.ValidateAndCapture(parent, child, store, now, "tenant-a");
        store.Set(Trust("service-a", "key-v2"));

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationTrustTransition.AssertUnchanged(snapshot, store));
    }

    [Fact]
    public void Verification_only_key_cannot_authorize_new_delegation()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new MemorySecurityWarrantDelegationTrustStateStore();
        var parent = Create("p", "root", "service-a");
        var child = Child(parent, keyId: "key-v1");
        var trust = Trust("service-a", "key-v1") with
        {
            KeyStates = new Dictionary<string, DelegationIssuerKeyState>
            {
                ["key-v1"] = DelegationIssuerKeyState.VerificationOnly
            }
        };
        store.Set(trust);

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationTrustTransition.ValidateAndCapture(parent, child, store, now, "tenant-a"));
    }

    [Fact]
    public void Retired_key_cannot_authorize_new_delegation()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new MemorySecurityWarrantDelegationTrustStateStore();
        var parent = Create("p", "root", "service-a");
        var child = Child(parent, keyId: "key-v1");
        store.Set(Trust("service-a", "key-v1") with
        {
            KeyStates = new Dictionary<string, DelegationIssuerKeyState>
            {
                ["key-v1"] = DelegationIssuerKeyState.Retired
            }
        });

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationTrustTransition.ValidateAndCapture(parent, child, store, now, "tenant-a"));
    }

    [Fact]
    public void Concurrent_trust_updates_produce_monotonic_sequences()
    {
        var store = new MemorySecurityWarrantDelegationTrustStateStore();
        store.Set(Trust("service-a"));
        var first = store.Capture("service-a", "child-key");
        store.Set(Trust("service-a", canDelegate: false));
        var second = store.Capture("service-a", "child-key");

        Assert.True(second.Sequence > first.Sequence);
        Assert.NotEqual(first.TrustFingerprint, second.TrustFingerprint);
    }

    private static DelegationIssuerTrust Trust(string issuer, string keyId = "child-key", bool canDelegate = true) =>
        new(issuer, new HashSet<string>([keyId]), canDelegate, "api", new HashSet<string>(["tenant-a"]));

    private static SecurityWarrant Create(string id, string issuer, string subject, string keyId = "root-key",
        DateTimeOffset? now = null)
    {
        var t = now ?? DateTimeOffset.UtcNow;
        return new SecurityWarrant(
            id, issuer, subject, "api", [new CapabilityGrant("Customer.read", "read", ["customer/*"])],
            new SecurityWarrantConstraints(allowedTenants: ["tenant-a"], allowedOperations: ["read"]),
            t.AddMinutes(-1), t.AddMinutes(10), $"nonce-{id}", keyId, null, []);
    }

    private static SecurityWarrant Child(SecurityWarrant parent, string subject = "service-b",
        string keyId = "child-key") =>
        parent with
        {
            Id = "child", Issuer = parent.Subject, Subject = subject, KeyId = keyId,
            ParentId = parent.Id, ParentDigest = parent.Digest, DelegationPath = [parent.Digest],
            Signature = [], ExpiresAt = parent.ExpiresAt.AddMinutes(-1)
        };
}