using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using System.Text.Json;
using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpApprovalTokenTests
{
    [Fact]
    public void DotNetSignerProducesByteForByteSharedGoldenToken()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Foundgine.MCP.Tests.mcp-approval-token-golden.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        using var key = RSA.Create();
        key.ImportFromPem(root.GetProperty("privateKeyPem_TEST_ONLY").GetString());
        var c = root.GetProperty("claims");
        var token = FoundgineMcpApprovalToken.Encode(
            c.GetProperty("approvalId").GetString()!, c.GetProperty("planFingerprint").GetString()!,
            c.GetProperty("approvedBy").GetString()!,
            DateTimeOffset.FromUnixTimeMilliseconds(c.GetProperty("approvedAtUnixMilliseconds").GetInt64()),
            c.GetProperty("requestJson").GetString()!,
            new FoundgineMcpRsaApprovalTokenSigner(c.GetProperty("keyId").GetString()!, key));
        Assert.Equal(root.GetProperty("token").GetString(), token);
    }

    [Fact]
    public void RsaTokenRoundTripsAndRejectsTampering()
    {
        using var key = RSA.Create(2048);
        var signer = new FoundgineMcpRsaApprovalTokenSigner("interop-key-1", key);
        var resolver = new TestResolver(key);
        const string request = "{\"operations\":[{\"entity\":1,\"kind\":\"Create\"}]}";
        var token = FoundgineMcpApprovalToken.Encode(
            "approval-1", "sha256:abc", "alice", DateTimeOffset.FromUnixTimeMilliseconds(1794592800123), request, signer);

        var claims = FoundgineMcpApprovalToken.DecodeAndVerify(token, resolver);
        Assert.Equal("approval-1", claims.ApprovalId);
        Assert.Equal("sha256:abc", claims.PlanFingerprint);
        Assert.Equal("alice", claims.ApprovedBy);
        Assert.Equal(1794592800123, claims.ApprovedAtUnixMilliseconds);
        Assert.Equal(request, claims.RequestJson);

        var parts = token.Split('.');
        var tampered = parts[0] + "." + parts[1] + "." + parts[2][..^2] + "AA";
        Assert.Throws<CryptographicException>(() => FoundgineMcpApprovalToken.DecodeAndVerify(tampered, resolver));
    }

    [Fact]
    public void VerifiesSharedGoldenVectorProducedOutsideDotNet()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Foundgine.MCP.Tests.mcp-approval-token-golden.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        using var key = RSA.Create();
        key.ImportFromPem(root.GetProperty("publicKeyPem").GetString());
        var resolver = new GoldenResolver(key);
        var claims = FoundgineMcpApprovalToken.DecodeAndVerify(root.GetProperty("token").GetString()!, resolver);
        var expected = root.GetProperty("claims");
        Assert.Equal(expected.GetProperty("keyId").GetString(), claims.KeyId);
        Assert.Equal(expected.GetProperty("approvalId").GetString(), claims.ApprovalId);
        Assert.Equal(expected.GetProperty("planFingerprint").GetString(), claims.PlanFingerprint);
        Assert.Equal(expected.GetProperty("approvedBy").GetString(), claims.ApprovedBy);
        Assert.Equal(expected.GetProperty("approvedAtUnixMilliseconds").GetInt64(), claims.ApprovedAtUnixMilliseconds);
        Assert.Equal(expected.GetProperty("requestJson").GetString(), claims.RequestJson);
    }

    private sealed class GoldenResolver(RSA key) : IFoundgineMcpApprovalTokenKeyResolver
    {
        public RSA? Resolve(string keyId) => keyId == "golden-key-1" ? key : null;
    }

    private sealed class TestResolver(RSA key) : IFoundgineMcpApprovalTokenKeyResolver
    {
        public RSA? Resolve(string keyId) => keyId == "interop-key-1" ? key : null;
    }
}
