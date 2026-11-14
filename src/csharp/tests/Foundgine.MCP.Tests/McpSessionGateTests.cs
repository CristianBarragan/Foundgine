using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpSessionGateTests
{
    private long _now = 1_000_000;

    private FoundgineMcpSessionGate Gate(int max = 10) => new(new FoundgineMcpSessionManager(
        new FoundgineMcpSessionOptions(TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), max),
        new InMemoryFoundgineMcpSessionStore(), () => _now));

    [Fact]
    public void InitializeIssuesASessionAndLaterRequestsNeedIt()
    {
        var g = Gate();
        var init = g.AdmitPost("initialize", null, "alice");
        Assert.True(init.Proceeds);
        Assert.NotNull(init.IssuedSessionId);
        var call = g.AdmitPost("tools/call", init.IssuedSessionId, "alice");
        Assert.True(call.Proceeds);
        Assert.Null(call.IssuedSessionId);
    }

    [Fact]
    public void MissingHeaderIs400AndUnknownMalformedOrForeignIs404()
    {
        var g = Gate();
        var id = g.AdmitPost("initialize", null, "alice").IssuedSessionId;
        Assert.Equal(400, g.AdmitPost("tools/list", null, "alice").HttpStatus);
        Assert.Equal(400, g.AdmitPost("tools/list", "  ", "alice").HttpStatus);
        Assert.Equal(404, g.AdmitPost("tools/list", "nope", "alice").HttpStatus);
        Assert.Equal(404, g.AdmitPost("tools/list", new string('a', 43), "alice").HttpStatus);
        Assert.Equal(404, g.AdmitPost("tools/list", id, "mallory").HttpStatus);
        Assert.Equal(400, g.AdmitPost(null, null, "alice").HttpStatus);
        Assert.True(g.AdmitPost(null, id, "alice").Proceeds);
    }

    [Fact]
    public void InitializeIgnoresAnyHeaderAndNeverReusesASession()
    {
        var g = Gate();
        var first = g.AdmitPost("initialize", null, "alice").IssuedSessionId;
        var second = g.AdmitPost("initialize", first, "alice");
        Assert.NotNull(second.IssuedSessionId);
        Assert.NotEqual(first, second.IssuedSessionId);
    }

    [Fact]
    public void DeleteTerminatesOnceAndOnlyForTheOwner()
    {
        var g = Gate();
        var id = g.AdmitPost("initialize", null, "alice").IssuedSessionId;
        Assert.Equal(404, g.AdmitDelete(id, "mallory").HttpStatus);
        Assert.Equal(400, g.AdmitDelete(null, "alice").HttpStatus);
        Assert.Equal(204, g.AdmitDelete(id, "alice").HttpStatus);
        Assert.Equal(404, g.AdmitDelete(id, "alice").HttpStatus);
        Assert.Equal(404, g.AdmitPost("tools/list", id, "alice").HttpStatus);
    }

    [Fact]
    public void ExpiredSessionsLookLikeUnknownOnes()
    {
        var g = Gate();
        var id = g.AdmitPost("initialize", null).IssuedSessionId;
        _now += (long)TimeSpan.FromMinutes(10).TotalMilliseconds;
        Assert.Equal(404, g.AdmitPost("tools/list", id).HttpStatus);
    }

    [Fact]
    public void FullSessionTableAnswers503()
    {
        var g = Gate(1);
        Assert.True(g.AdmitPost("initialize", null).Proceeds);
        var full = g.AdmitPost("initialize", null);
        Assert.False(full.Proceeds);
        Assert.Equal(503, full.HttpStatus);
    }

    [Fact]
    public void GetIsAdmittedOnlyForALiveOwnedSession()
    {
        var g = Gate();
        var id = g.AdmitPost("initialize", null, "alice").IssuedSessionId;
        Assert.True(g.AdmitGet(id, "alice").Proceeds);
        Assert.Equal(400, g.AdmitGet(null, "alice").HttpStatus);
        Assert.Equal(400, g.AdmitGet("  ", "alice").HttpStatus);
        Assert.Equal(404, g.AdmitGet(id, "mallory").HttpStatus);
        Assert.Equal(404, g.AdmitGet("nope", "alice").HttpStatus);
        g.AdmitDelete(id, "alice");
        Assert.Equal(404, g.AdmitGet(id, "alice").HttpStatus);
    }
}
