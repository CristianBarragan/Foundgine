using System.Security.Cryptography;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Core.Semantic.Tests.Security.Warrants;

public sealed class SecurityWarrantTests
{
    [Fact]
    public void Canonical_signature_round_trips_and_digest_is_stable()
    {
        using var key = RSA.Create(2048);
        var now = DateTimeOffset.UtcNow;
        var signed = Sign(Create(now), key);

        SecurityWarrantVerifier.Verify(signed, new Resolver(signed.KeyId, key), now.AddMinutes(1), "issuer",
            "foundgine");
        Assert.NotEmpty(signed.Signature);
        Assert.Equal(signed.Digest, SecurityWarrantCanonicalizer.Digest(signed));
    }

    [Fact]
    public void Expired_warrant_is_rejected() =>
        AssertVerificationFails(Create(DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1)));

    [Fact]
    public void Wrong_issuer_is_rejected() =>
        AssertVerificationFails(Create(DateTimeOffset.UtcNow, issuer: "other"), expectedIssuer: "issuer");

    [Fact]
    public void Wrong_audience_is_rejected() =>
        AssertVerificationFails(Create(DateTimeOffset.UtcNow, audience: "other"), expectedAudience: "foundgine");

    [Fact]
    public void Wrong_subject_is_rejected_at_runtime()
    {
        var w = Create(DateTimeOffset.UtcNow);
        Assert.False(SecurityWarrantAuthorization.Allows(w, "wrong-subject", w.Audience, "Customer.read", "read",
            "tenant-1", "customer/*"));
    }

    [Fact]
    public void Wrong_tenant_is_rejected_at_runtime()
    {
        var w = Create(DateTimeOffset.UtcNow);
        Assert.False(SecurityWarrantAuthorization.Allows(w, w.Subject, w.Audience, "Customer.read", "read", "tenant-2",
            "customer/*"));
    }

    [Fact]
    public void Wrong_capability_is_rejected_at_runtime()
    {
        var w = Create(DateTimeOffset.UtcNow);
        Assert.False(SecurityWarrantAuthorization.Allows(w, w.Subject, w.Audience, "Customer.delete", "read",
            "tenant-1", "customer/*"));
    }

    [Fact]
    public void Wrong_resource_is_rejected_at_runtime()
    {
        var w = Create(DateTimeOffset.UtcNow);
        Assert.False(SecurityWarrantAuthorization.Allows(w, w.Subject, w.Audience, "Customer.read", "read", "tenant-1",
            "order/*"));
    }

    [Fact]
    public void Wrong_operation_is_rejected_at_runtime()
    {
        var w = Create(DateTimeOffset.UtcNow);
        Assert.False(SecurityWarrantAuthorization.Allows(w, w.Subject, w.Audience, "Customer.read", "write", "tenant-1",
            "customer/*"));
    }

    [Fact]
    public void Modified_warrant_fails_signature_verification() =>
        AssertSignatureFails(w => w with { Subject = "attacker" });

    [Fact]
    public void Modified_constraint_fails_signature_verification() =>
        AssertSignatureFails(w => w with { Constraints = w.Constraints with { MaxAmount = 999999m } });

    [Fact]
    public void Modified_signature_fails_verification() => AssertSignatureFails(w =>
        w with { Signature = w.Signature.Select((x, i) => i == 0 ? (byte)(x ^ 0xFF) : x).ToArray() });

    [Fact]
    public void Wrong_key_is_rejected()
    {
        using var key = RSA.Create(2048);
        using var other = RSA.Create(2048);
        var signed = Sign(Create(DateTimeOffset.UtcNow), key);
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantVerifier.Verify(signed, new Resolver(signed.KeyId, other), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Unknown_key_is_rejected()
    {
        using var key = RSA.Create(2048);
        var signed = Sign(Create(DateTimeOffset.UtcNow), key);
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantVerifier.Verify(signed, new EmptyResolver(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Algorithm_substitution_is_rejected()
    {
        using var key = RSA.Create(2048);
        var w = Create(DateTimeOffset.UtcNow);
        var substituted = w with
        {
            Signature = key.SignData(SecurityWarrantCanonicalizer.UnsignedBytes(w), HashAlgorithmName.SHA512,
                RSASignaturePadding.Pkcs1)
        };
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantVerifier.Verify(substituted, new Resolver(w.KeyId, key), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Signature_over_noncanonical_representation_is_rejected()
    {
        using var key = RSA.Create(2048);
        var w = Create(DateTimeOffset.UtcNow);
        var nonCanonical = SecurityWarrantCanonicalizer.UnsignedJson(w)
            .Replace("{\"id\"", "{ \"id\"", StringComparison.Ordinal);
        var signature = key.SignData(System.Text.Encoding.UTF8.GetBytes(nonCanonical), HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantVerifier.Verify(w with { Signature = signature }, new Resolver(w.KeyId, key),
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Child_grants_more_than_parent_is_rejected() => AssertDelegationFails((p, c) =>
        c with { Grants = [new CapabilityGrant("Customer.write", "write", ["customer/*"])] });

    [Fact]
    public void Child_extends_expiry_is_rejected() =>
        AssertDelegationFails((p, c) => c with { ExpiresAt = p.ExpiresAt.AddMinutes(1) });

    [Fact]
    public void Child_changes_tenant_is_rejected() => AssertDelegationFails((p, c) =>
        c with { Constraints = c.Constraints with { AllowedTenants = ["tenant-2"] } });

    [Fact]
    public void Child_adds_capability_is_rejected() => AssertDelegationFails((p, c) =>
        c with { Grants = [.. p.Grants, new CapabilityGrant("Customer.delete", "delete", ["customer/*"])] });

    [Fact]
    public void Child_changes_resource_scope_is_rejected() => AssertDelegationFails((p, c) =>
        c with { Grants = [new CapabilityGrant("Customer.read", "read", ["*"])] });
    // A child changing Subject is intentionally NOT an attenuation violation — see
    // Delegated_subject_can_change_but_issuer_must_be_parent_subject below, which
    // asserts that delegating to a new subject succeeds as long as the child's
    // Issuer equals the parent's Subject. There is deliberately no
    // "Child_changes_subject_is_rejected" test.

    [Fact]
    public void Same_warrant_same_nonce_can_only_be_consumed_once()
    {
        var store = new MemorySecurityWarrantReplayStore();
        var w = Create(DateTimeOffset.UtcNow);
        SecurityWarrantReplayGuard.Consume(w, store, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantReplayGuard.Consume(w, store, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("different-intent")]
    [InlineData("different-tenant")]
    [InlineData("different-amount")]
    [InlineData("different-target")]
    public void Same_warrant_cannot_be_reused_for_a_different_intent(string _)
    {
        var store = new MemorySecurityWarrantReplayStore();
        var w = Create(DateTimeOffset.UtcNow);
        SecurityWarrantReplayGuard.Consume(w, store, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantReplayGuard.Consume(w, store, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Same_warrant_after_expiry_is_rejected()
    {
        var store = new MemorySecurityWarrantReplayStore();
        var issued = DateTimeOffset.UtcNow.AddMinutes(-10);
        var w = Create(issued, issued, issued.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantReplayGuard.Consume(w, store, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Delegated_subject_can_change_but_issuer_must_be_parent_subject()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create(now);
        var child = parent with
        {
            Id = "child", Subject = "agent-b", ParentId = parent.Id, Issuer = parent.Subject,
            ParentDigest = parent.Digest, DelegationPath = [parent.Digest], Signature = [],
            ExpiresAt = parent.ExpiresAt.AddMinutes(-1)
        };
        Assert.Same(child, SecurityWarrantAttenuator.Attenuate(parent, child, now));
    }

    [Fact]
    public void Parent_digest_substitution_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create(now);
        var other = Create(now) with { Id = "other" };
        var child = parent with
        {
            Id = "child", ParentId = parent.Id, Issuer = parent.Subject, ParentDigest = other.Digest,
            DelegationPath = [other.Digest], Signature = []
        };
        Assert.Throws<InvalidOperationException>(() => SecurityWarrantAttenuator.Attenuate(parent, child, now));
    }

    [Fact]
    public void Delegation_path_substitution_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create(now);
        var child = parent with
        {
            Id = "child", ParentId = parent.Id, Issuer = parent.Subject, ParentDigest = parent.Digest,
            DelegationPath = ["forged-parent"], Signature = []
        };
        Assert.Throws<InvalidOperationException>(() => SecurityWarrantAttenuator.Attenuate(parent, child, now));
    }

    [Fact]
    public void Delegation_cycle_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create(now);
        var child = parent with
        {
            Id = "child", ParentId = parent.Id, Issuer = parent.Subject, ParentDigest = parent.Digest,
            DelegationPath = [parent.Digest, parent.Digest], Signature = []
        };
        Assert.Throws<InvalidOperationException>(() => SecurityWarrantAttenuator.Attenuate(parent, child, now));
    }

    [Fact]
    public void Delegation_depth_cannot_skip_levels()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create(now);
        var child = parent with
        {
            Id = "child", ParentId = parent.Id, Issuer = parent.Subject, ParentDigest = parent.Digest,
            DelegationPath = [], Signature = []
        };
        Assert.Throws<InvalidOperationException>(() => SecurityWarrantAttenuator.Attenuate(parent, child, now));
    }

    [Fact]
    public void Valid_child_can_only_attenuate_parent()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create(now) with
        {
            Constraints = new SecurityWarrantConstraints(allowedTenants: ["tenant-1"], maxResults: 100)
        };
        var child = parent with
        {
            Id = "child", ParentId = parent.Id, Issuer = parent.Subject,
            ExpiresAt = parent.ExpiresAt.AddMinutes(-1), Signature = [],
            ParentDigest = parent.Digest, DelegationPath = [parent.Digest],
            Constraints = new SecurityWarrantConstraints(allowedTenants: ["tenant-1"], maxResults: 20)
        };
        Assert.Same(child, SecurityWarrantAttenuator.Attenuate(parent, child, now));
    }

    [Fact]
    public void Runtime_authorization_requires_current_subject_audience_tenant_resource_and_limits()
    {
        var w = Create(DateTimeOffset.UtcNow) with
        {
            Constraints =
            new SecurityWarrantConstraints(allowedTenants: ["tenant-1"], maxResults: 10, maxAmount: 100m)
        };
        Assert.True(SecurityWarrantAuthorization.Allows(w, "agent-a", "foundgine", "Customer.read", "read", "tenant-1",
            "customer/*", 5, 50));
        Assert.False(SecurityWarrantAuthorization.Allows(w, "attacker", "foundgine", "Customer.read", "read",
            "tenant-1", "customer/*", 5, 50));
        Assert.False(SecurityWarrantAuthorization.Allows(w, "agent-a", "wrong-audience", "Customer.read", "read",
            "tenant-1", "customer/*", 5, 50));
        Assert.False(SecurityWarrantAuthorization.Allows(w, "agent-a", "foundgine", "Customer.read", "read", "tenant-2",
            "customer/*", 5, 50));
        Assert.False(SecurityWarrantAuthorization.Allows(w, "agent-a", "foundgine", "Customer.read", "read", "tenant-1",
            "order/*", 5, 50));
        Assert.False(SecurityWarrantAuthorization.Allows(w, "agent-a", "foundgine", "Customer.read", "read", "tenant-1",
            "customer/*", 11, 50));
        Assert.False(SecurityWarrantAuthorization.Allows(w, "agent-a", "foundgine", "Customer.read", "read", "tenant-1",
            "customer/*", 5, 101));
    }

    private static SecurityWarrant Create(DateTimeOffset now, DateTimeOffset? issuedAt = null,
        DateTimeOffset? expiresAt = null, string issuer = "issuer", string audience = "foundgine") => new(
        "warrant-1", issuer, "agent-a", audience,
        [new CapabilityGrant("Customer.read", "read", ["customer/*"])],
        new SecurityWarrantConstraints(allowedTenants: ["tenant-1"], maxResults: 100, maxAmount: 1000m),
        issuedAt ?? now.AddMinutes(-1), expiresAt ?? now.AddHours(1), "nonce-1", "key-1", null, []);

    private static SecurityWarrant Sign(SecurityWarrant warrant, RSA key) => SecurityWarrantSigner.Sign(warrant, key);

    private static void AssertVerificationFails(SecurityWarrant warrant, string? expectedIssuer = null,
        string? expectedAudience = null)
    {
        using var key = RSA.Create(2048);
        var signed = Sign(warrant, key);
        Assert.Throws<InvalidOperationException>(() => SecurityWarrantVerifier.Verify(signed,
            new Resolver(signed.KeyId, key), DateTimeOffset.UtcNow, expectedIssuer, expectedAudience));
    }

    private static void AssertSignatureFails(Func<SecurityWarrant, SecurityWarrant> mutate)
    {
        using var key = RSA.Create(2048);
        var signed = Sign(Create(DateTimeOffset.UtcNow), key);
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantVerifier.Verify(mutate(signed), new Resolver(signed.KeyId, key), DateTimeOffset.UtcNow));
    }

    private static void AssertDelegationFails(Func<SecurityWarrant, SecurityWarrant, SecurityWarrant> mutate)
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create(now) with
        {
            Constraints = new SecurityWarrantConstraints(allowedTenants: ["tenant-1"],
                resourceScopes: ["customer/*"], maxResults: 100)
        };
        var child = parent with
        {
            Id = "child", ParentId = parent.Id, Issuer = parent.Subject,
            ExpiresAt = parent.ExpiresAt.AddMinutes(-1), Signature = [], ParentDigest = parent.Digest,
            DelegationPath = [parent.Digest]
        };
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantAttenuator.Attenuate(parent, mutate(parent, child), now));
    }

    private sealed class Resolver(string id, RSA key) : ISecurityWarrantKeyResolver
    {
        public RSA Resolve(string keyId) => StringComparer.Ordinal.Equals(id, keyId)
            ? key
            : throw new InvalidOperationException("Unknown key");
    }

    private sealed class EmptyResolver : ISecurityWarrantKeyResolver
    {
        public RSA Resolve(string keyId) => throw new InvalidOperationException("Unknown key");
    }
}

public sealed class SecurityWarrantDelegationTrustSecurityTests
{
    private sealed class Resolver(params DelegationIssuerTrust[] trusts) : ISecurityWarrantDelegationTrustResolver
    {
        public DelegationIssuerTrust? Resolve(string issuer) =>
            trusts.FirstOrDefault(x => StringComparer.Ordinal.Equals(x.Issuer, issuer));
    }

    private static SecurityWarrant Create(string id, string issuer, string subject, string keyId = "issuer-key",
        DateTimeOffset? now = null)
    {
        var t = now ?? DateTimeOffset.UtcNow;
        return new SecurityWarrant(
            id, issuer, subject, "api", [new CapabilityGrant("Customer.read", "read", ["customer/*"])],
            new SecurityWarrantConstraints(allowedTenants: ["tenant-a"], allowedOperations: ["read"]),
            t.AddMinutes(-1), t.AddMinutes(10), $"nonce-{id}", keyId, null, []) { };
    }

    private static SecurityWarrant Child(SecurityWarrant parent, string subject = "service-b",
        string keyId = "child-key") =>
        parent with
        {
            Id = "child", Issuer = parent.Subject, Subject = subject, KeyId = keyId,
            ParentId = parent.Id, ParentDigest = parent.Digest, DelegationPath = [parent.Digest],
            Signature = [], ExpiresAt = parent.ExpiresAt.AddMinutes(-1)
        };

    [Fact]
    public void Non_delegating_issuer_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create("p", "root", "service-a");
        var child = Child(parent);
        var trust = new Resolver(new DelegationIssuerTrust("service-a", new HashSet<string>(["child-key"]), false));
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationTrust.VerifyIssuer(parent, child, trust, now, "tenant-a"));
    }

    [Fact]
    public void Unknown_issuer_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create("p", "root", "service-a");
        var child = Child(parent);
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationTrust.VerifyIssuer(parent, child, new Resolver(), now, "tenant-a"));
    }

    [Fact]
    public void Key_substitution_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create("p", "root", "service-a");
        var child = Child(parent, keyId: "forged-key");
        var trust = new Resolver(new DelegationIssuerTrust("service-a", new HashSet<string>(["child-key"]), true));
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationTrust.VerifyIssuer(parent, child, trust, now, "tenant-a"));
    }

    [Fact]
    public void Execute_only_issuer_cannot_delegate()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create("p", "root", "service-a");
        var child = Child(parent);
        var trust = new Resolver(new DelegationIssuerTrust("service-a", new HashSet<string>(["child-key"]), false));
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationTrust.VerifyIssuer(parent, child, trust, now));
    }

    [Fact]
    public void Trusted_issuer_with_active_delegation_key_is_accepted()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create("p", "root", "service-a");
        var child = Child(parent);
        var trust = new Resolver(new DelegationIssuerTrust("service-a", new HashSet<string>(["child-key"]), true));
        SecurityWarrantDelegationTrust.VerifyIssuer(parent, child, trust, now, "tenant-a");
    }

    [Fact]
    public void Audience_scope_is_enforced()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create("p", "root", "service-a");
        var child = Child(parent) with { Audience = "other-api" };
        var trust = new Resolver(
            new DelegationIssuerTrust("service-a", new HashSet<string>(["child-key"]), true, "api"));
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationTrust.VerifyIssuer(parent, child, trust, now));
    }

    [Fact]
    public void Tenant_scope_is_enforced()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create("p", "root", "service-a");
        var child = Child(parent);
        var trust = new Resolver(new DelegationIssuerTrust("service-a", new HashSet<string>(["child-key"]), true,
            AllowedTenants: new HashSet<string>(["tenant-b"])));
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationTrust.VerifyIssuer(parent, child, trust, now, "tenant-a"));
    }

    [Fact]
    public void Issuer_must_be_parent_subject()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Create("p", "root", "service-a");
        var child = Child(parent) with { Issuer = "service-c" };
        var trust = new Resolver(new DelegationIssuerTrust("service-c", new HashSet<string>(["child-key"]), true));
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantDelegationTrust.VerifyIssuer(parent, child, trust, now));
    }
}