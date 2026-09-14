using System.Security.Cryptography;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Security.Tests.Penetration;

/// <summary>SEC-39..SEC-42: identity binding and cryptographic mutation attacks.</summary>
public sealed class CryptographicAndIdentityPenetrationTests
{
    [Fact]
    public void Subject_rebinding_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        using var key = RSA.Create(2048);
        var warrant = Sign(Create(now, subject: "alice"), key);

        Assert.False(SecurityWarrantAuthorization.Allows(warrant, "bob", "api", "Account.read", "read", "tenant-a",
            "account-1"));
    }

    [Fact]
    public void Audience_rebinding_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        using var key = RSA.Create(2048);
        var warrant = Sign(Create(now, audience: "api-a"), key);

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantVerifier.Verify(warrant, new Resolver(warrant.KeyId, key), now, "issuer", "api-b"));
    }

    [Fact]
    public void Tenant_and_resource_constraints_fail_closed_when_runtime_context_is_missing()
    {
        var now = DateTimeOffset.UtcNow;
        using var key = RSA.Create(2048);
        var warrant = Sign(Create(now, constraints: new SecurityWarrantConstraints(
            allowedTenants: ["tenant-a"], resourceScopes: ["account-1"])), key);

        Assert.False(SecurityWarrantAuthorization.Allows(warrant, "alice", "api", "Account.read", "read", null,
            "account-1"));
        Assert.False(SecurityWarrantAuthorization.Allows(warrant, "alice", "api", "Account.read", "read", "tenant-a",
            null));
    }

    [Fact]
    public void Single_byte_signature_mutation_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        using var key = RSA.Create(2048);
        var warrant = Sign(Create(now), key);
        var signature = warrant.Signature.ToArray();
        signature[signature.Length / 2] ^= 0x01;

        Assert.Throws<InvalidOperationException>(() => SecurityWarrantVerifier.Verify(
            warrant with { Signature = signature }, new Resolver(warrant.KeyId, key), now, "issuer", "api"));
    }

    [Fact]
    public void Signed_payload_mutation_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        using var key = RSA.Create(2048);
        var warrant = Sign(Create(now), key);
        var mutated = warrant with { Subject = "attacker" };

        Assert.Throws<InvalidOperationException>(() => SecurityWarrantVerifier.Verify(
            mutated, new Resolver(warrant.KeyId, key), now, "issuer", "api"));
    }

    [Fact]
    public void Key_identifier_substitution_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        using var key = RSA.Create(2048);
        var warrant = Sign(Create(now), key);

        Assert.Throws<InvalidOperationException>(() => SecurityWarrantVerifier.Verify(
            warrant with { KeyId = "attacker-key" }, new Resolver(warrant.KeyId, key), now, "issuer", "api"));
    }

    [Fact]
    public void Algorithm_substitution_cannot_turn_an_RSA_warrant_into_an_accepted_unknown_scheme()
    {
        // The warrant format has one fixed verification algorithm. There is no
        // algorithm field that an attacker can downgrade or substitute.
        var canonical =
            typeof(SecurityWarrantCanonicalizer).GetMethod(nameof(SecurityWarrantCanonicalizer.UnsignedJson));
        Assert.NotNull(canonical);
        Assert.DoesNotContain("algorithm", SecurityWarrantCanonicalizer.UnsignedJson(Create(DateTimeOffset.UtcNow)),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Canonicalization_changes_when_security_authority_changes()
    {
        var now = DateTimeOffset.UtcNow;
        var a = Create(now, constraints: new SecurityWarrantConstraints(allowedTenants: ["tenant-a"]));
        var b = a with { Constraints = new SecurityWarrantConstraints(allowedTenants: ["tenant-b"]) };

        Assert.NotEqual(a.Digest, b.Digest);
    }

    private static SecurityWarrant Create(
        DateTimeOffset now,
        string issuer = "issuer",
        string subject = "alice",
        string audience = "api",
        SecurityWarrantConstraints? constraints = null) =>
        new(
            "warrant-1", issuer, subject, audience,
            [new CapabilityGrant("Account.read", "read", ["account-1"])],
            constraints ?? SecurityWarrantConstraints.Unrestricted,
            now.AddMinutes(-1), now.AddMinutes(10), "nonce-1", "key-1", null, []);

    private static SecurityWarrant Sign(SecurityWarrant warrant, RSA key) => SecurityWarrantSigner.Sign(warrant, key);

    private sealed class Resolver(string id, RSA key) : ISecurityWarrantKeyResolver
    {
        public RSA Resolve(string keyId) => StringComparer.Ordinal.Equals(id, keyId)
            ? key
            : throw new InvalidOperationException("unknown key");
    }
}