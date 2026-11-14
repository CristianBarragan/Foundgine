using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpApprovalKeyRotationTests
{
    private const string Request = "{\"operations\":[{\"entity\":1,\"kind\":\"Create\",\"fields\":{\"2\":\"x\"}}]}";
    private static readonly TimeSpan FiveMin30S = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30);

    private static string Token(FoundgineMcpApprovalKeyRing ring, string approvalId) =>
        FoundgineMcpApprovalToken.Encode(approvalId, "sha256:abc", "alice",
            DateTimeOffset.FromUnixTimeMilliseconds(1_000), Request, ring);

    [Fact]
    public void SignsWithActiveKeyAndVerifiesWithSameRing()
    {
        var ring = new FoundgineMcpApprovalKeyRing();
        using var k1 = RSA.Create(2048);
        ring.ActivateSigningKey("k1", k1);
        var claims = FoundgineMcpApprovalToken.DecodeAndVerify(Token(ring, "a1"), ring);
        Assert.Equal("k1", claims.KeyId);
        Assert.Equal("a1", claims.ApprovalId);
    }

    [Fact]
    public void RotationKeepsOutstandingTokensVerifiableUntilRetentionEnds()
    {
        long now = 1_000_000;
        var ring = new FoundgineMcpApprovalKeyRing(FiveMin30S, () => now);
        using var k1 = RSA.Create(2048);
        using var k2 = RSA.Create(2048);
        ring.ActivateSigningKey("k1", k1);
        var oldToken = Token(ring, "old");
        ring.ActivateSigningKey("k2", k2, FiveMin30S);
        var newToken = Token(ring, "new");

        Assert.Equal("k2", FoundgineMcpApprovalToken.DecodeAndVerify(newToken, ring).KeyId);
        Assert.Equal("k1", FoundgineMcpApprovalToken.DecodeAndVerify(oldToken, ring).KeyId);
        Assert.Equal("k2", ring.KeyId);

        now += (long)FiveMin30S.TotalMilliseconds - 1;
        FoundgineMcpApprovalToken.DecodeAndVerify(oldToken, ring);
        now += 1;
        Assert.Throws<CryptographicException>(() => FoundgineMcpApprovalToken.DecodeAndVerify(oldToken, ring));
        FoundgineMcpApprovalToken.DecodeAndVerify(newToken, ring);
    }

    [Fact]
    public void SnapshotPinsTheKeyAcrossRotationAndOldKeyCannotSign()
    {
        var ring = new FoundgineMcpApprovalKeyRing();
        using var k1 = RSA.Create(2048);
        using var k2 = RSA.Create(2048);
        ring.ActivateSigningKey("k1", k1);
        var pinned = ring.Snapshot();
        ring.ActivateSigningKey("k2", k2, TimeSpan.FromMinutes(10));
        Assert.Equal("k1", pinned.KeyId);
        Assert.Equal("k2", ring.Snapshot().KeyId);
        var infos = ring.Keys();
        Assert.Equal(2, infos.Count);
        Assert.Equal(FoundgineMcpApprovalKeyRole.Signing, infos[0].Role);
        Assert.Equal("k2", infos[0].KeyId);
        Assert.Equal(FoundgineMcpApprovalKeyRole.VerifyOnly, infos[1].Role);
    }

    [Fact]
    public void RetentionBelowPolicyMinimumIsRejectedAndNothingChanges()
    {
        var policy = new FoundgineMcpApprovalTokenPolicy(new AlwaysConsume());
        Assert.Equal(FiveMin30S, policy.MinimumKeyRetention);
        var ring = FoundgineMcpApprovalKeyRing.ForPolicy(policy);
        using var k1 = RSA.Create(2048);
        using var k2 = RSA.Create(2048);
        ring.ActivateSigningKey("k1", k1);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.ActivateSigningKey("k2", k2, TimeSpan.FromMinutes(1)));
        Assert.Equal("k1", ring.KeyId);
        Assert.Null(ring.Resolve("k2"));
        Assert.Throws<InvalidOperationException>(() => ring.ActivateSigningKey("k2", k2));
    }

    [Fact]
    public void RevocationIsImmediateAndKeyIdsAreNeverReused()
    {
        var ring = new FoundgineMcpApprovalKeyRing();
        using var k1 = RSA.Create(2048);
        using var k2 = RSA.Create(2048);
        using var attacker = RSA.Create(2048);
        ring.ActivateSigningKey("k1", k1);
        var token = Token(ring, "a");
        ring.ActivateSigningKey("k2", k2, TimeSpan.FromDays(1));
        Assert.True(ring.Revoke("k1"));
        Assert.Null(ring.Resolve("k1"));
        Assert.Throws<CryptographicException>(() => FoundgineMcpApprovalToken.DecodeAndVerify(token, ring));
        Assert.Throws<ArgumentException>(() => ring.AddVerificationKey("k1", attacker));
        Assert.True(ring.Revoke("k2"));
        Assert.Throws<InvalidOperationException>(() => ring.KeyId);
        using var k3 = RSA.Create(2048);
        ring.ActivateSigningKey("k3", k3);
        Assert.Equal("k3", ring.KeyId);
    }

    [Fact]
    public void VerifyOnlyHostsFollowRotationAndNeverSign()
    {
        long now = 5_000;
        var verifier = new FoundgineMcpApprovalKeyRing(TimeSpan.FromMinutes(1), () => now);
        using var k1 = RSA.Create(2048);
        using var k2 = RSA.Create(2048);
        var signer = new FoundgineMcpApprovalKeyRing();
        signer.ActivateSigningKey("k1", k1);
        verifier.AddVerificationKey("k1", k1);
        Assert.Throws<InvalidOperationException>(() => verifier.KeyId);
        var t1 = Token(signer, "a");
        Assert.Equal("a", FoundgineMcpApprovalToken.DecodeAndVerify(t1, verifier).ApprovalId);

        verifier.AddVerificationKey("k2", k2);
        Assert.Throws<ArgumentOutOfRangeException>(() => verifier.RetireAfter("k1", TimeSpan.FromSeconds(1)));
        Assert.True(verifier.RetireAfter("k1", TimeSpan.FromMinutes(5)));
        Assert.True(verifier.RetireAfter("k1", TimeSpan.FromMinutes(10)));   // never extends
        now += (long)TimeSpan.FromMinutes(5).TotalMilliseconds;
        Assert.Throws<CryptographicException>(() => FoundgineMcpApprovalToken.DecodeAndVerify(t1, verifier));
        Assert.False(verifier.RetireAfter("k1", TimeSpan.FromMinutes(5)));
        Assert.Throws<ArgumentException>(() =>
            verifier.AddVerificationKey("late", k2, DateTimeOffset.FromUnixTimeMilliseconds(now)));
    }

    [Fact]
    public void RejectsWeakAndBlankKeys()
    {
        var ring = new FoundgineMcpApprovalKeyRing();
        using var weak = RSA.Create(1024);
        using var ok = RSA.Create(2048);
        Assert.Throws<ArgumentException>(() => ring.ActivateSigningKey("w", weak));
        Assert.Throws<ArgumentException>(() => ring.ActivateSigningKey(" ", ok));
        Assert.Throws<InvalidOperationException>(() => ring.KeyId);
        Assert.Null(ring.Resolve("nope"));
    }

    [Fact]
    public void TokenFromForeignKeyMaterialUnderSameIdIsRejected()
    {
        var ring = new FoundgineMcpApprovalKeyRing();
        var foreign = new FoundgineMcpApprovalKeyRing();
        using var k1 = RSA.Create(2048);
        using var other = RSA.Create(2048);
        foreign.ActivateSigningKey("k1", other);
        ring.ActivateSigningKey("k1", k1);
        Assert.Throws<CryptographicException>(() => FoundgineMcpApprovalToken.DecodeAndVerify(Token(foreign, "forged"), ring));
    }

    [Fact]
    public void SharedGoldenVectorStillVerifiesAndRegeneratesThroughTheRing()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Foundgine.MCP.Tests.mcp-approval-token-golden.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        using var key = RSA.Create();
        key.ImportFromPem(root.GetProperty("privateKeyPem_TEST_ONLY").GetString());
        var ring = new FoundgineMcpApprovalKeyRing();
        ring.ActivateSigningKey("golden-key-1", key);
        var expectedToken = root.GetProperty("token").GetString()!;
        Assert.Equal("golden-approval-1", FoundgineMcpApprovalToken.DecodeAndVerify(expectedToken, ring).ApprovalId);
        var c = root.GetProperty("claims");
        var regenerated = FoundgineMcpApprovalToken.Encode(
            c.GetProperty("approvalId").GetString()!, c.GetProperty("planFingerprint").GetString()!,
            c.GetProperty("approvedBy").GetString()!,
            DateTimeOffset.FromUnixTimeMilliseconds(c.GetProperty("approvedAtUnixMilliseconds").GetInt64()),
            c.GetProperty("requestJson").GetString()!, ring);
        Assert.Equal(expectedToken, regenerated);
    }

    private sealed class AlwaysConsume : IFoundgineMcpApprovalTokenReplayStore
    {
        public bool TryConsume(string approvalId, long expiresAtUnixMilliseconds) => true;
    }
}
