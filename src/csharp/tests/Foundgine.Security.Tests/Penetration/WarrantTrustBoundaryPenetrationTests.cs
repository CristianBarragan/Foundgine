using System.Security.Cryptography;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Security.Tests.Penetration;

/// <summary>
/// issuer, delegation and replay trust controls: attacks against the warrant trust boundary itself.
/// These tests assert that issuer trust and delegation ancestry are now
/// fail-closed, while the replay tests distinguish process-local memory state
/// from the durable/shared implementations required for multi-instance hosts.
/// </summary>
public sealed class WarrantTrustBoundaryPenetrationTests
{
    // ------------------------------------------------------------------
    // SEC-25: issuer trust is now fail-closed. A missing trusted issuer is a
    // configuration error rather than an implicit trust decision.
    // ------------------------------------------------------------------

    [Fact]
    public void Forged_issuer_is_rejected_when_expected_issuer_is_configured()
    {
        using var key = RSA.Create(2048);
        var forged = Sign(Create(DateTimeOffset.UtcNow, issuer: "attacker-controlled-issuer"), key);

        // A correctly configured host supplies the trusted root issuer.
        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantVerifier.Verify(
                forged,
                new Resolver(forged.KeyId, key),
                DateTimeOffset.UtcNow,
                expectedIssuer: "trusted-root-issuer"));
    }

    [Fact]
    public void ATTACK_forged_issuer_is_rejected_when_expected_issuer_is_left_unconfigured()
    {
        using var key = RSA.Create(2048);
        var forged = Sign(Create(DateTimeOffset.UtcNow, issuer: "attacker-controlled-issuer"), key);

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantVerifier.Verify(
                forged,
                new Resolver(forged.KeyId, key),
                DateTimeOffset.UtcNow,
                expectedIssuer: null));
    }

    // ------------------------------------------------------------------
    // SEC-26: delegated warrants now require a complete root-to-leaf chain and
    // an explicit delegation trust resolver at the execution boundary.
    // ------------------------------------------------------------------

    [Fact]
    public void ATTACK_delegated_warrant_without_complete_chain_is_rejected()
    {
        using var key = RSA.Create(2048);
        var now = DateTimeOffset.UtcNow;
        var uncheckedChild = Sign(
            Create(now, issuer: "root-issuer") with
            {
                Id = "child-1",
                ParentId = "never-verified-parent",
                ParentDigest = new string('0', 128),
                DelegationPath = [new string('0', 128)],
            },
            key);

        Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantExecutionTrust.Verify(
                uncheckedChild,
                new Resolver(uncheckedChild.KeyId, key),
                "root-issuer",
                uncheckedChild.Audience,
                now,
                suppliedChain: null,
                delegationTrust: new TrustResolver(),
                tenant: null));
    }

    // ------------------------------------------------------------------
    // SEC-27: the memory implementation remains intentionally process-local.
    // A durable implementation is provided for shared-filesystem deployments;
    // distributed deployments should supply a shared transactional store.
    // ------------------------------------------------------------------

    [Fact]
    public void ATTACK_replay_guard_does_not_share_state_across_store_instances()
    {
        var w = Create(DateTimeOffset.UtcNow);

        var instanceA = new MemorySecurityWarrantReplayStore();
        var instanceB = new MemorySecurityWarrantReplayStore();

        // Consumed once on "instance A" (e.g. one replica behind a load balancer).
        SecurityWarrantReplayGuard.Consume(w, instanceA, DateTimeOffset.UtcNow);

        // The identical warrant, replayed against an independent, uncoordinated
        // "instance B", is accepted again - there is nothing in the shipped
        // MemorySecurityWarrantReplayStore that makes this fail.
        var exception = Record.Exception(() =>
            SecurityWarrantReplayGuard.Consume(w, instanceB, DateTimeOffset.UtcNow));

        Assert.Null(exception);

        // A horizontally scaled deployment using the default in-memory store
        // (rather than a shared/distributed store) effectively grants one
        // replay per replica for every warrant, for the lifetime of the
        // warrant's validity window.
    }

    [Fact]
    public void Durable_replay_store_shares_consumption_across_instances()
    {
        var path = Path.Combine(Path.GetTempPath(), $"foundgine-replay-{Guid.NewGuid():N}.log");
        try
        {
            var instanceA = new FileSecurityWarrantReplayStore(path);
            var instanceB = new FileSecurityWarrantReplayStore(path);

            Assert.True(instanceA.TryConsume("warrant", "nonce"));
            Assert.False(instanceB.TryConsume("warrant", "nonce"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
        }
    }

    private static SecurityWarrant Create(
        DateTimeOffset now,
        string issuer = "issuer",
        string audience = "foundgine") => new(
        "warrant-1", issuer, "agent-a", audience,
        [new CapabilityGrant("Customer.read", "read", ["customer/*"])],
        new SecurityWarrantConstraints(allowedTenants: ["tenant-1"], maxResults: 100, maxAmount: 1000m),
        now.AddMinutes(-1), now.AddHours(1), "nonce-1", "key-1", null, []);

    private static SecurityWarrant Sign(SecurityWarrant warrant, RSA key) =>
        SecurityWarrantSigner.Sign(warrant, key);

    private sealed class TrustResolver : ISecurityWarrantDelegationTrustResolver
    {
        public DelegationIssuerTrust? Resolve(string issuer) =>
            new(issuer, new HashSet<string>(StringComparer.Ordinal) { "key-1" }, true);
    }

    private sealed class Resolver(string id, RSA key) : ISecurityWarrantKeyResolver
    {
        public RSA Resolve(string keyId) =>
            StringComparer.Ordinal.Equals(id, keyId) ? key : throw new InvalidOperationException("Unknown key");
    }
}