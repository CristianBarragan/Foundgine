using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpSessionManagerTests
{
    private long _now = 1_000_000;
    private static long Ms(TimeSpan t) => (long)t.TotalMilliseconds;

    private FoundgineMcpSessionManager Manager(int max = 10) => new(
        new FoundgineMcpSessionOptions(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(25), max),
        new InMemoryFoundgineMcpSessionStore(), () => _now);

    [Fact]
    public void IssuesOpaqueUniqueUrlSafeIds()
    {
        var m = Manager(1000);
        var ids = new HashSet<string>();
        for (var i = 0; i < 200; i++)
        {
            var id = m.Create().SessionId;
            Assert.Equal(43, id.Length);
            Assert.True(FoundgineMcpSessionManager.IsWellFormed(id));
            Assert.True(ids.Add(id));
            Assert.All(id, c => Assert.InRange(c, (char)0x21, (char)0x7e));
        }
    }

    [Fact]
    public void SlidesIdleExpiryButNeverPastTheAbsoluteLimit()
    {
        var m = Manager();
        var s = m.Create();
        Assert.Equal(_now + Ms(TimeSpan.FromMinutes(10)), s.ExpiresAtUnixMilliseconds);
        _now += Ms(TimeSpan.FromMinutes(9));
        var v = m.Validate(s.SessionId);
        Assert.True(v.IsValid);
        Assert.Equal(_now + Ms(TimeSpan.FromMinutes(10)), v.Session!.ExpiresAtUnixMilliseconds);
        _now += Ms(TimeSpan.FromMinutes(9));
        Assert.True(m.Validate(s.SessionId).IsValid);
        _now += Ms(TimeSpan.FromMinutes(5));   // 23 min since creation; absolute limit is 25
        var last = m.Validate(s.SessionId);
        Assert.True(last.IsValid);
        Assert.Equal(s.CreatedAtUnixMilliseconds + Ms(TimeSpan.FromMinutes(25)), last.Session!.ExpiresAtUnixMilliseconds);
        _now += Ms(TimeSpan.FromMinutes(2));
        Assert.Equal(FoundgineMcpSessionStatus.Expired, m.Validate(s.SessionId).Status);
        Assert.Equal(FoundgineMcpSessionStatus.NotFound, m.Validate(s.SessionId).Status);
    }

    [Fact]
    public void IdleSessionExpires()
    {
        var m = Manager();
        var s = m.Create();
        _now += Ms(TimeSpan.FromMinutes(10));
        Assert.Equal(FoundgineMcpSessionStatus.Expired, m.Validate(s.SessionId).Status);
    }

    [Fact]
    public void TerminationIsExplicitIdempotentAndFinal()
    {
        var m = Manager();
        var s = m.Create();
        Assert.True(m.Terminate(s.SessionId));
        Assert.False(m.Terminate(s.SessionId));
        Assert.Equal(FoundgineMcpSessionStatus.NotFound, m.Validate(s.SessionId).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!")]
    [InlineData("../../../../../../../../../../../../../../.")]
    public void MalformedUnknownAndNullIdsAreNotFound(string? bad)
    {
        var m = Manager();
        m.Create();
        Assert.Equal(FoundgineMcpSessionStatus.NotFound, m.Validate(bad).Status);
        Assert.False(m.Terminate(bad));
    }

    [Fact]
    public void OwnerBindingStopsAnotherPrincipalFromUsingOrEndingASession()
    {
        var m = Manager();
        var s = m.Create("alice");
        Assert.Equal(FoundgineMcpSessionStatus.OwnerMismatch, m.Validate(s.SessionId, "mallory").Status);
        Assert.Equal(FoundgineMcpSessionStatus.OwnerMismatch, m.Validate(s.SessionId).Status);
        Assert.False(m.Terminate(s.SessionId, "mallory"));
        Assert.False(m.Terminate(s.SessionId));
        Assert.True(m.Validate(s.SessionId, "alice").IsValid);
        Assert.True(m.Terminate(s.SessionId, "alice"));
    }

    [Fact]
    public void ASessionCarriesNoIdentityOrAuthority()
    {
        var names = typeof(FoundgineMcpSession).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToArray();
        Assert.Equal(4, names.Length);
        Assert.DoesNotContain(names, n => n.Contains("owner") || n.Contains("subject"));
    }

    [Fact]
    public void EnforcesSessionCapAndReclaimsExpiredSessions()
    {
        var m = Manager(2);
        m.Create();
        m.Create();
        Assert.Throws<InvalidOperationException>(() => m.Create());
        _now += Ms(TimeSpan.FromMinutes(10));
        Assert.NotNull(m.Create());
        Assert.Equal(1, m.ActiveSessionCount);
    }

    [Fact]
    public void PurgeRemovesOnlyExpired()
    {
        var m = Manager();
        m.Create();
        _now += Ms(TimeSpan.FromMinutes(6));
        var keep = m.Create();
        _now += Ms(TimeSpan.FromMinutes(5));
        Assert.Equal(1, m.PurgeExpired());
        Assert.True(m.Validate(keep.SessionId).IsValid);
    }

    [Fact]
    public void StoreDoesNotHoldTheRawId()
    {
        var store = new InMemoryFoundgineMcpSessionStore();
        var m = new FoundgineMcpSessionManager(store: store, unixMillisecondsClock: () => _now);
        var s = m.Create();
        Assert.Null(store.Find(s.SessionId));
    }

    [Fact]
    public void OptionsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FoundgineMcpSessionOptions(TimeSpan.Zero, TimeSpan.FromHours(1), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FoundgineMcpSessionOptions(TimeSpan.FromHours(2), TimeSpan.FromHours(1), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FoundgineMcpSessionOptions(TimeSpan.FromHours(1), TimeSpan.FromHours(1), 0));
    }
}
