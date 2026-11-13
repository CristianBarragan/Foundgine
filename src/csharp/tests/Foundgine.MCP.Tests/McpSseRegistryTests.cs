using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpSseRegistryTests
{
    private static readonly string A = new('A', 43), B = new('B', 43), C = new('C', 43);
    private long _now = 1_000_000;

    [Fact]
    public void OneBufferPerSessionAndLeastRecentlyUsedIsDropped()
    {
        var reg = new FoundgineMcpSseRegistry(2, 5, 100);
        var a = reg.Stream(A);
        Assert.Same(a, reg.Stream(A));
        var b = reg.Stream(B);
        Assert.NotSame(a, b);
        reg.Stream(A);   // A most recent, B least
        reg.Stream(C);   // evicts B
        Assert.Equal(2, reg.Count);
        Assert.Same(a, reg.Stream(A));
        Assert.NotSame(b, reg.Stream(B));   // B was recreated empty
    }

    [Fact]
    public void RemoveAndIllFormedIds()
    {
        var reg = new FoundgineMcpSseRegistry();
        reg.Stream(A);
        Assert.True(reg.Remove(A));
        Assert.False(reg.Remove(A));
        Assert.Throws<ArgumentException>(() => reg.Stream("short"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FoundgineMcpSseRegistry(0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FoundgineMcpSseRegistry(1, 0, 1));
    }

    [Fact]
    public void LeavingTheRegistryRetiresTheBuffer()
    {
        var reg = new FoundgineMcpSseRegistry(1, 5, 100);
        var a = reg.Stream(A);
        reg.Stream(B);   // evicts A
        Assert.True(a.IsRetired);
        var b = reg.Find(B)!;
        Assert.False(b.IsRetired);
        Assert.True(reg.Remove(B));
        Assert.True(b.IsRetired);
    }

    [Fact]
    public void SweepDropsOnlyBuffersOfSessionsThatAreNotLive()
    {
        var reg = new FoundgineMcpSseRegistry();
        var a = reg.Stream(A);
        var b = reg.Stream(B);
        var told = new List<string>();
        a.Subscribe(() => { }, () => told.Add("a"));
        Assert.Equal(1, reg.Sweep(id => id == B));
        Assert.Null(reg.Find(A));
        Assert.Same(b, reg.Find(B));
        Assert.Equal(new[] { "a" }, told);
        Assert.Equal(0, reg.Sweep(_ => true));
    }

    [Fact]
    public void IsLiveDoesNotSlideTheDeadlineOrCheckTheOwner()
    {
        var manager = new FoundgineMcpSessionManager(
            new FoundgineMcpSessionOptions(TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), 10),
            new InMemoryFoundgineMcpSessionStore(), () => _now);
        var id = manager.Create("alice").SessionId;
        Assert.True(manager.IsLive(id));
        Assert.False(manager.IsLive("short"));
        Assert.False(manager.IsLive(A));
        _now += 9 * 60_000;
        Assert.True(manager.IsLive(id));    // still inside the idle window ...
        _now += 2 * 60_000;
        Assert.False(manager.IsLive(id));   // ... and the check above did not extend it
    }
}
