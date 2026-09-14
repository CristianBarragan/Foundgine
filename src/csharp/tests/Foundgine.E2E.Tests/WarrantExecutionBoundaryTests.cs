using System.Security.Cryptography;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.E2E.Tests;

public sealed class WarrantExecutionBoundaryTests
{
    [Fact]
    public void Signed_warrant_authorizes_the_declared_capability()
    {
        using var key = RSA.Create(2048);
        var warrant = CreateWarrant(key);
        var resolver = new TestKeyResolver(warrant.KeyId, key);

        SecurityWarrantVerifier.Verify(warrant, resolver, DateTimeOffset.UtcNow, "issuer", "api");
        Assert.True(SecurityWarrantAuthorization.Allows(
            warrant, "alice", "api", "customers.read", "read", "tenant-a", "tenant-a"));
    }

    [Fact]
    public void Wrong_subject_is_rejected()
    {
        using var key = RSA.Create(2048);
        var warrant = CreateWarrant(key);

        Assert.False(SecurityWarrantAuthorization.Allows(
            warrant, "mallory", "api", "customers.read", "read", "tenant-a", "tenant-a"));
    }

    [Fact]
    public void Wrong_tenant_is_rejected()
    {
        using var key = RSA.Create(2048);
        var warrant = CreateWarrant(key);

        Assert.False(SecurityWarrantAuthorization.Allows(
            warrant, "alice", "api", "customers.read", "read", "tenant-b", "tenant-a"));
    }

    [Fact]
    public void Replay_guard_rejects_second_execution()
    {
        using var key = RSA.Create(2048);
        var warrant = CreateWarrant(key);
        var store = new MemorySecurityWarrantReplayStore();
        var now = DateTimeOffset.UtcNow;

        SecurityWarrantReplayGuard.Consume(warrant, store, now);
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantReplayGuard.Consume(warrant, store, now));
    }

    private static SecurityWarrant CreateWarrant(RSA key)
    {
        var now = DateTimeOffset.UtcNow;
        return SecurityWarrantSigner.Sign(new SecurityWarrant(
            "w-1",
            "issuer",
            "alice",
            "api",
            [new CapabilityGrant("customers.read", "read", ["tenant-a"])],
            new SecurityWarrantConstraints(allowedTenants: ["tenant-a"], resourceScopes: ["tenant-a"]),
            now.AddMinutes(-1),
            now.AddMinutes(10),
            "nonce-1",
            "key-1",
            null,
            []), key);
    }

    private sealed class TestKeyResolver(string id, RSA key) : ISecurityWarrantKeyResolver
    {
        public RSA Resolve(string keyId) =>
            StringComparer.Ordinal.Equals(id, keyId) ? key : throw new InvalidOperationException();
    }
}