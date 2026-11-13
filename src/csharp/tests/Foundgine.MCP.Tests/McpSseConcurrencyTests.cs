using System.Collections.Concurrent;
using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

/// <summary>Contention tests for the SSE buffer, registry and transport. Threads are released together; each wait has a hard timeout.</summary>
public sealed class McpSseConcurrencyTests
{
    private const int Threads = 16;

    private static void Race(Action<int> task)
    {
        using var go = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, Threads).Select(i => Task.Factory.StartNew(() => { go.Wait(); task(i); },
            TaskCreationOptions.LongRunning)).ToArray();
        go.Set();
        Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(60)), "threads did not finish");   // WaitAll rethrows the first failure
    }

    private sealed class NoopSink : IFoundgineMcpSseSink
    {
        public void Write(string frame) { }
        public void Complete() { }
    }

    private static FoundgineMcpSessionGate Gate() => new(new FoundgineMcpSessionManager(
        new FoundgineMcpSessionOptions(TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), 100), new InMemoryFoundgineMcpSessionStore()));

    [Fact]
    public void AppendDuringReplayNeverShowsAGapOrADuplicate()
    {
        const int perThread = 200;
        var buffer = new FoundgineMcpSseBuffer(100_000, 100_000_000L);
        var ids = new ConcurrentDictionary<string, byte>();
        Race(n =>
        {
            if (n % 2 == 0)
            {
                for (var i = 0; i < perThread; i++) Assert.True(ids.TryAdd(buffer.Append($"m{n}-{i}").Id, 0), "duplicate id");
            }
            else
            {
                for (var i = 0; i < perThread; i++)
                {
                    var replay = buffer.ReplayAfter("0");
                    Assert.Equal(FoundgineMcpSseReplayStatus.Ok, replay.Status);
                    long expected = 1;
                    foreach (var e in replay.Events) Assert.True(expected++ == e.Sequence, "replay must be gapless and ascending");
                }
            }
        });
        Assert.Equal((long)(Threads / 2) * perThread, buffer.LastSequence);
        Assert.Equal(buffer.LastSequence, ids.Count);
    }

    [Fact]
    public void LruHoldsItsBoundAndRetiresWhatItEvictsUnderContention()
    {
        const int max = 8;
        var registry = new FoundgineMcpSseRegistry(max, 4, 1000);
        var seen = new ConcurrentBag<FoundgineMcpSseBuffer>();
        Race(n =>
        {
            for (var i = 0; i < 300; i++)
            {
                var b = registry.Stream(($"s{(n * 31 + i * 7) % 64}" + new string('x', 43))[..43]);   // must be a well-formed 43-char id
                Assert.NotNull(b);
                seen.Add(b);
                Assert.True(registry.Count <= max, "bound exceeded");
            }
        });
        Assert.Equal(max, registry.Count);
        Assert.Equal(max, seen.Distinct().Count(b => !b.IsRetired));   // exactly the buffers still in the registry are unretired
    }

    [Fact]
    public void SimultaneousGetsOnOneSessionYieldOneStreamAndTheRestAreBusy()
    {
        var gate = Gate();
        var registry = new FoundgineMcpSseRegistry();
        var settings = new FoundgineMcpSseTransport.Settings(null, 0, new FoundgineMcpSseLimits(1000, 1000), null);
        var id = gate.AdmitPost("initialize", null, "alice").IssuedSessionId;
        var statuses = new ConcurrentBag<int>();
        var streams = new ConcurrentBag<FoundgineMcpSseStream>();
        Race(_ =>
        {
            var r = FoundgineMcpSseTransport.Get(gate, registry, settings, null, id, "alice", null, new NoopSink(), work => work());
            statuses.Add(r.Status);
            if (r.Stream is not null) streams.Add(r.Stream);
        });
        Assert.Equal(1, statuses.Count(s => s == 200));
        Assert.Equal(Threads - 1, statuses.Count(s => s == 409));
        foreach (var s in streams) s.Close();
    }

    [Fact]
    public void PerPrincipalLimitIsExactUnderContention()
    {
        const int cap = 3;
        var gate = Gate();
        var registry = new FoundgineMcpSseRegistry();
        var settings = new FoundgineMcpSseTransport.Settings(null, 0, new FoundgineMcpSseLimits(1000, cap), null);
        var sessions = Enumerable.Range(0, Threads).Select(_ => gate.AdmitPost("initialize", null, "alice").IssuedSessionId).ToArray();
        var statuses = new ConcurrentBag<int>();
        var streams = new ConcurrentBag<FoundgineMcpSseStream>();
        Race(n =>
        {
            var r = FoundgineMcpSseTransport.Get(gate, registry, settings, null, sessions[n], "alice", null, new NoopSink(), work => work());
            statuses.Add(r.Status);
            if (r.Stream is not null) streams.Add(r.Stream);
        });
        Assert.Equal(cap, statuses.Count(s => s == 200));
        Assert.Equal(Threads - cap, statuses.Count(s => s == 429));
        foreach (var s in streams) s.Close();
    }
}
