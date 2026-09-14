using System.Security.Cryptography;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Core.Semantic.Tests.Security.Warrants;

/// <summary>
/// Guard-rail tests for authority monotonicity, fail-closed context handling,
/// canonicalization and replay behavior. These tests intentionally attack the
/// boundaries rather than only exercising successful authorization paths.
/// </summary>
public sealed class SecurityWarrantGuardRailsPenetrationTests
{
    [Fact]
    public void Tenant_restriction_fails_closed_when_runtime_tenant_is_missing()
    {
        var warrant = CreateWarrant(constraints: new SecurityWarrantConstraints(allowedTenants: ["tenant-a"]));

        Assert.False(SecurityWarrantAuthorization.Allows(
            warrant, warrant.Subject, warrant.Audience,
            "Customer.read", "read", null, "customer/1"));
    }

    [Fact]
    public void Tenant_restriction_rejects_a_different_runtime_tenant()
    {
        var warrant = CreateWarrant(constraints: new SecurityWarrantConstraints(allowedTenants: ["tenant-a"]));

        Assert.False(SecurityWarrantAuthorization.Allows(
            warrant, warrant.Subject, warrant.Audience,
            "Customer.read", "read", "tenant-b", "customer/1"));
    }

    [Fact]
    public void Resource_restriction_fails_closed_when_runtime_resource_is_missing()
    {
        var warrant = CreateWarrant(constraints: new SecurityWarrantConstraints(resourceScopes: ["customer/1"]));

        Assert.False(SecurityWarrantAuthorization.Allows(
            warrant, warrant.Subject, warrant.Audience,
            "Customer.read", "read", "tenant-a", null));
    }

    [Fact]
    public void Resource_restriction_rejects_a_different_runtime_resource()
    {
        var warrant = CreateWarrant(constraints: new SecurityWarrantConstraints(resourceScopes: ["customer/1"]));

        Assert.False(SecurityWarrantAuthorization.Allows(
            warrant, warrant.Subject, warrant.Audience,
            "Customer.read", "read", "tenant-a", "customer/2"));
    }

    [Fact]
    public void Capability_matching_is_exact_not_prefix_based()
    {
        var warrant = CreateWarrant(grants: [new CapabilityGrant("Customer.read", "read")]);

        Assert.False(SecurityWarrantAuthorization.Allows(
            warrant, warrant.Subject, warrant.Audience,
            "Customer.read.admin", "read", "tenant-a", "customer/1"));
    }

    [Fact]
    public void Operation_matching_is_exact_not_prefix_based()
    {
        var warrant = CreateWarrant(grants: [new CapabilityGrant("Customer.read", "read")]);

        Assert.False(SecurityWarrantAuthorization.Allows(
            warrant, warrant.Subject, warrant.Audience,
            "Customer.read", "read.all", "tenant-a", "customer/1"));
    }

    [Fact]
    public void Unicode_confusable_capability_does_not_match()
    {
        // The second character in the requested capability is Cyrillic 'е'.
        const string confusable = "Customer.rеad";
        var warrant = CreateWarrant(grants: [new CapabilityGrant("Customer.read", "read")]);

        Assert.False(SecurityWarrantAuthorization.Allows(
            warrant, warrant.Subject, warrant.Audience,
            confusable, "read", "tenant-a", "customer/1"));
    }

    [Fact]
    public void Canonicalization_is_stable_under_grant_and_scope_ordering()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateWarrant(
            now,
            [
                new CapabilityGrant("Customer.write", "write", ["customer/2", "customer/1"]),
                new CapabilityGrant("Customer.read", "read", ["customer/3", "customer/1"])
            ],
            new SecurityWarrantConstraints(
                allowedTenants: ["tenant-b", "tenant-a"],
                allowedFields: ["Name", "Id"],
                resourceScopes: ["customer/2", "customer/1"],
                allowedOperations: ["write", "read"]));

        var second = first with
        {
            Grants =
            [
                new CapabilityGrant("Customer.read", "read", ["customer/1", "customer/3"]),
                new CapabilityGrant("Customer.write", "write", ["customer/1", "customer/2"])
            ],
            Constraints = new SecurityWarrantConstraints(
                allowedTenants: ["tenant-a", "tenant-b"],
                allowedFields: ["Id", "Name"],
                resourceScopes: ["customer/1", "customer/2"],
                allowedOperations: ["read", "write"])
        };

        Assert.Equal(
            SecurityWarrantCanonicalizer.Digest(first),
            SecurityWarrantCanonicalizer.Digest(second));
    }

    [Fact]
    public void Changing_any_security_semantic_changes_the_digest()
    {
        var original = CreateWarrant();
        var variants = new[]
        {
            original with { Subject = "another-agent" },
            original with { Audience = "another-audience" },
            original with { Nonce = "another-nonce" },
            original with { KeyId = "another-key" },
            original with { ExpiresAt = original.ExpiresAt.AddSeconds(1) },
            original with { ParentId = "parent" },
            original with { ParentDigest = "parent-digest" },
            original with { DelegationPath = ["ancestor"] },
            original with { Constraints = original.Constraints with { MaxResults = 1 } }
        };

        foreach (var variant in variants)
            Assert.NotEqual(original.Digest, variant.Digest);
    }

    [Fact]
    public void Signature_binds_the_security_semantics_not_just_the_identifier()
    {
        using var key = RSA.Create(2048);
        var original = SecurityWarrantSigner.Sign(CreateWarrant(), key);
        var modified = original with
        {
            Constraints = original.Constraints with { MaxAmount = 1m }
        };

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantVerifier.Verify(modified, new Resolver(original.KeyId, key), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Replay_consumption_is_atomic_under_concurrency()
    {
        var warrant = CreateWarrant();
        var store = new MemorySecurityWarrantReplayStore();
        var successes = 0;

        Parallel.For(0, 64, _ =>
        {
            try
            {
                SecurityWarrantReplayGuard.Consume(warrant, store, DateTimeOffset.UtcNow);
                Interlocked.Increment(ref successes);
            }
            catch (InvalidOperationException)
            {
                // Expected for every contender except the single winner.
            }
        });

        Assert.Equal(1, successes);
    }

    [Fact]
    public void Replay_key_is_bound_to_both_warrant_id_and_nonce()
    {
        var store = new MemorySecurityWarrantReplayStore();
        var first = CreateWarrant(id: "w1", nonce: "n1");
        var second = CreateWarrant(id: "w2", nonce: "n2");

        SecurityWarrantReplayGuard.Consume(first, store, DateTimeOffset.UtcNow);
        SecurityWarrantReplayGuard.Consume(second, store, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Expired_warrant_cannot_be_consumed_even_when_nonce_is_new()
    {
        var now = DateTimeOffset.UtcNow;
        var warrant = CreateWarrant(now.AddHours(-2), now.AddHours(-1));
        var store = new MemorySecurityWarrantReplayStore();

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantReplayGuard.Consume(warrant, store, now));
    }

    [Fact]
    public void Child_authority_is_monotonic_for_capabilities_and_constraints()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = CreateWarrant(
            now,
            [new CapabilityGrant("Customer.read", "read", ["customer/1", "customer/2"])],
            new SecurityWarrantConstraints(
                allowedTenants: ["tenant-a"],
                allowedFields: ["Id", "Name"],
                resourceScopes: ["customer/1", "customer/2"],
                allowedOperations: ["read"],
                maxResults: 100,
                maxAmount: 100m));

        var child = parent with
        {
            Id = "child",
            Issuer = parent.Subject,
            Subject = "agent-child",
            ParentId = parent.Id,
            ParentDigest = parent.Digest,
            DelegationPath = [parent.Digest],
            Signature = [],
            ExpiresAt = parent.ExpiresAt.AddMinutes(-1),
            Constraints = new SecurityWarrantConstraints(
                allowedTenants: ["tenant-a"],
                allowedFields: ["Id"],
                resourceScopes: ["customer/1"],
                allowedOperations: ["read"],
                maxResults: 10,
                maxAmount: 10m)
        };

        Assert.Same(child, SecurityWarrantAttenuator.Attenuate(parent, child, now));
    }

    [Fact]
    public void Child_cannot_recover_a_removed_tenant()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = CreateWarrant(now,
            constraints: new SecurityWarrantConstraints(allowedTenants: ["tenant-a", "tenant-b"]));
        var attenuated = parent with
        {
            Id = "child",
            Issuer = parent.Subject,
            Subject = "agent-child",
            ParentId = parent.Id,
            ParentDigest = parent.Digest,
            DelegationPath = [parent.Digest],
            Signature = [],
            ExpiresAt = parent.ExpiresAt.AddMinutes(-1),
            Constraints = new SecurityWarrantConstraints(allowedTenants: ["tenant-a"])
        };
        var grandchild = attenuated with
        {
            Id = "grandchild",
            Issuer = attenuated.Subject,
            Subject = "agent-grandchild",
            ParentId = attenuated.Id,
            ParentDigest = attenuated.Digest,
            DelegationPath = [parent.Digest, attenuated.Digest],
            Signature = [],
            Constraints = new SecurityWarrantConstraints(allowedTenants: ["tenant-a", "tenant-b"])
        };

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantAttenuator.Attenuate(attenuated, grandchild, now));
    }

    private static SecurityWarrant CreateWarrant(
        DateTimeOffset? now = null,
        IReadOnlyList<CapabilityGrant>? grants = null,
        SecurityWarrantConstraints? constraints = null,
        string id = "warrant",
        string nonce = "nonce")
    {
        var current = now ?? DateTimeOffset.UtcNow;
        return new SecurityWarrant(
            id,
            "issuer",
            "agent",
            "foundgine",
            grants ?? [new CapabilityGrant("Customer.read", "read", ["customer/1"])],
            constraints ?? SecurityWarrantConstraints.Unrestricted,
            current.AddMinutes(-1),
            current.AddHours(1),
            nonce,
            "key-1",
            null,
            []);
    }

    private static SecurityWarrant CreateWarrant(DateTimeOffset issuedAt, DateTimeOffset expiresAt) =>
        CreateWarrant(expiresAt.AddHours(-1)) with { IssuedAt = issuedAt, ExpiresAt = expiresAt };

    private sealed class Resolver(string keyId, RSA key) : ISecurityWarrantKeyResolver
    {
        public RSA Resolve(string requestedKeyId) =>
            string.Equals(requestedKeyId, keyId, StringComparison.Ordinal)
                ? key
                : throw new InvalidOperationException("Unknown key.");
    }
}