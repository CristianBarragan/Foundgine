using System.Security.Cryptography;
using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpApprovalTokenPolicyTests
{
    [Fact]
    public void EnforcesExpiryFutureSkewAndAtomicSingleUse()
    {
        const long now = 1_000_000;
        var store = new InMemoryReplayStore();
        var policy = new FoundgineMcpApprovalTokenPolicy(store, TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(30), () => now);
        var claims = new FoundgineMcpApprovalTokenClaims("k", "a1", "fp", "alice", now - 1_000, "{}");
        policy.ValidateAndConsume(claims);
        Assert.Throws<CryptographicException>(() => policy.ValidateAndConsume(claims));
        Assert.Throws<CryptographicException>(() => policy.ValidateAndConsume(
            claims with { ApprovalId = "old", ApprovedAtUnixMilliseconds = now - 300_001 }));
        Assert.Throws<CryptographicException>(() => policy.ValidateAndConsume(
            claims with { ApprovalId = "future", ApprovedAtUnixMilliseconds = now + 30_001 }));
    }

    private sealed class InMemoryReplayStore : IFoundgineMcpApprovalTokenReplayStore
    {
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
        public bool TryConsume(string approvalId, long expiresAtUnixMilliseconds)
        { lock (_ids) return _ids.Add(approvalId); }
    }
}
