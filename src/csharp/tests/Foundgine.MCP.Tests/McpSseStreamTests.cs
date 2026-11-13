using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpSseStreamTests
{
    private static readonly string Session = new('A', 43);

    private sealed class Recorder : IFoundgineMcpSseSink
    {
        public readonly List<string> Frames = new();
        public bool Completed;
        public bool FailWrites;
        public void Write(string frame)
        {
            if (FailWrites) throw new IOException("client gone");
            Frames.Add(frame);
        }
        public void Complete() => Completed = true;
    }

    private sealed class Log : IFoundgineMcpAuditSink
    {
        public readonly List<FoundgineMcpAuditEvent> Events = new();
        public void Record(FoundgineMcpAuditEvent auditEvent) => Events.Add(auditEvent);
    }

    private sealed class ThrowingLog : IFoundgineMcpAuditSink
    {
        public void Record(FoundgineMcpAuditEvent auditEvent) => throw new InvalidOperationException("boom");
    }

    private static readonly Action<Action> Inline = work => work();

    private static string Frame(long id, string data) => $"id: {id}\ndata: {data}\n\n";

    private static FoundgineMcpSseStreamOptions Options(FoundgineMcpSseLimits? limits = null, long lifetime = 0,
        IFoundgineMcpAuditSink? audit = null, Func<long>? clock = null) => new(Session, "alice", limits, lifetime, audit, clock);

    [Fact]
    public void ReplaysAfterLastEventIdThenDeliversLiveEventsInOrderExactlyOnce()
    {
        var buffer = new FoundgineMcpSseBuffer(10, 10_000);
        buffer.Append("a"); buffer.Append("b");
        var sink = new Recorder();
        var opened = FoundgineMcpSseStream.Open(buffer, "1", sink, Inline);
        Assert.Equal(FoundgineMcpSseOpenStatus.Ok, opened.Status);
        opened.Stream!.Start();
        Assert.Equal(new[] { Frame(2, "b") }, sink.Frames);
        buffer.Append("c"); buffer.Append("d");
        Assert.Equal(new[] { Frame(2, "b"), Frame(3, "c"), Frame(4, "d") }, sink.Frames);
    }

    [Fact]
    public void NoLastEventIdMeansOnlyNewEvents()
    {
        var buffer = new FoundgineMcpSseBuffer(10, 10_000);
        buffer.Append("old");
        var sink = new Recorder();
        FoundgineMcpSseStream.Open(buffer, null, sink, Inline).Stream!.Start();
        Assert.Empty(sink.Frames);
        buffer.Append("new");
        Assert.Equal(new[] { Frame(2, "new") }, sink.Frames);
    }

    [Fact]
    public void OnlyOneStreamPerSessionAndFailedOpensFreeTheSlot()
    {
        var buffer = new FoundgineMcpSseBuffer(2, 10_000);
        buffer.Append("a"); buffer.Append("b"); buffer.Append("c");   // keeps b, c
        var sink = new Recorder();
        Assert.Equal(FoundgineMcpSseOpenStatus.Evicted, FoundgineMcpSseStream.Open(buffer, "0", sink, Inline).Status);
        Assert.Equal(FoundgineMcpSseOpenStatus.Invalid, FoundgineMcpSseStream.Open(buffer, "9", sink, Inline).Status);
        var first = FoundgineMcpSseStream.Open(buffer, null, sink, Inline);
        Assert.Equal(FoundgineMcpSseOpenStatus.Ok, first.Status);
        Assert.Equal(FoundgineMcpSseOpenStatus.Busy, FoundgineMcpSseStream.Open(buffer, null, sink, Inline).Status);
        first.Stream!.Close();
        Assert.Equal(FoundgineMcpSseOpenStatus.Ok, FoundgineMcpSseStream.Open(buffer, null, sink, Inline).Status);
    }

    [Fact]
    public void AWriteFailureClosesTheStreamWithoutFailingThePublisher()
    {
        var buffer = new FoundgineMcpSseBuffer(10, 10_000);
        var sink = new Recorder();
        var stream = FoundgineMcpSseStream.Open(buffer, null, sink, Inline).Stream!;
        stream.Start();
        sink.FailWrites = true;
        buffer.Append("x");
        Assert.True(stream.IsClosed);
        Assert.True(sink.Completed);
    }

    [Fact]
    public void AClientTooSlowToKeepUpIsClosedRatherThanSkipped()
    {
        var buffer = new FoundgineMcpSseBuffer(2, 10_000);
        var sink = new Recorder();
        var queued = new List<Action>();
        var stream = FoundgineMcpSseStream.Open(buffer, null, sink, queued.Add).Stream!;
        stream.Start();
        buffer.Append("1"); buffer.Append("2"); buffer.Append("3");   // 1 is evicted before the writer ever ran
        foreach (var work in queued.ToArray()) work();
        Assert.True(stream.IsClosed);
        Assert.Empty(sink.Frames);   // never delivers 2 and 3 while silently skipping 1
    }

    [Fact]
    public void HeartbeatWritesACommentAndStopsAfterClose()
    {
        var sink = new Recorder();
        var stream = FoundgineMcpSseStream.Open(new FoundgineMcpSseBuffer(10, 10_000), null, sink, Inline).Stream!;
        stream.Heartbeat();
        Assert.Equal(new[] { ": keep-alive\n\n" }, sink.Frames);
        stream.Close();
        stream.Heartbeat();
        Assert.Single(sink.Frames);
    }

    [Fact]
    public void StreamLimitsAreEnforcedPerPrincipalAndReleasedOnClose()
    {
        var limits = new FoundgineMcpSseLimits(10, 1);
        var one = FoundgineMcpSseStream.Open(new FoundgineMcpSseBuffer(5, 1000), null, new Recorder(), Inline, Options(limits));
        Assert.Equal(FoundgineMcpSseOpenStatus.Ok, one.Status);
        var refused = FoundgineMcpSseStream.Open(new FoundgineMcpSseBuffer(5, 1000), null, new Recorder(), Inline, Options(limits));
        Assert.Equal(FoundgineMcpSseOpenStatus.Limit, refused.Status);
        Assert.Equal(1, limits.Open);
        one.Stream!.Close();
        Assert.Equal(0, limits.Open);
        Assert.Equal(FoundgineMcpSseOpenStatus.Ok,
            FoundgineMcpSseStream.Open(new FoundgineMcpSseBuffer(5, 1000), null, new Recorder(), Inline, Options(limits)).Status);
    }

    [Fact]
    public void ARefusedOpenReleasesItsPermit()
    {
        var limits = new FoundgineMcpSseLimits(10, 10);
        var buffer = new FoundgineMcpSseBuffer(5, 1000);
        var first = FoundgineMcpSseStream.Open(buffer, null, new Recorder(), Inline, Options(limits));
        Assert.Equal(FoundgineMcpSseOpenStatus.Busy, FoundgineMcpSseStream.Open(buffer, null, new Recorder(), Inline, Options(limits)).Status);
        Assert.Equal(FoundgineMcpSseOpenStatus.Invalid,
            FoundgineMcpSseStream.Open(new FoundgineMcpSseBuffer(5, 1000), "9", new Recorder(), Inline, Options(limits)).Status);
        Assert.Equal(1, limits.Open);   // only the first stream holds a permit
        first.Stream!.Close();
        Assert.Equal(0, limits.Open);
    }

    [Fact]
    public void AStreamPastItsMaximumLifetimeEndsAtTheNextHeartbeat()
    {
        long now = 0;
        var log = new Log();
        var sink = new Recorder();
        var stream = FoundgineMcpSseStream.Open(new FoundgineMcpSseBuffer(5, 1000), null, sink, Inline,
            Options(null, 1000, log, () => now)).Stream!;
        stream.Start();
        now = 999;
        stream.Heartbeat();
        Assert.False(stream.IsClosed);
        now = 1000;
        stream.Heartbeat();
        Assert.True(stream.IsClosed);
        Assert.True(sink.Completed);
        Assert.Equal("reason=lifetime", log.Events[^1].Detail);
    }

    [Fact]
    public void RetiringTheBufferClosesTheStreamAndFreesItsPermit()
    {
        var limits = new FoundgineMcpSseLimits(10, 10);
        var log = new Log();
        var buffer = new FoundgineMcpSseBuffer(5, 1000);
        var sink = new Recorder();
        var stream = FoundgineMcpSseStream.Open(buffer, null, sink, Inline, Options(limits, 0, log)).Stream!;
        stream.Start();
        buffer.Retire();
        Assert.True(stream.IsClosed);
        Assert.True(sink.Completed);
        Assert.Equal(0, limits.Open);
        Assert.Equal("reason=session_ended", log.Events[^1].Detail);
        Assert.Equal(FoundgineMcpSseOpenStatus.Gone,
            FoundgineMcpSseStream.Open(buffer, null, new Recorder(), Inline, Options(limits, 0, log)).Status);
        Assert.Equal("status=GONE", log.Events[^1].Detail);
    }

    [Fact]
    public void AuditEventsCarryTheFingerprintNeverTheSessionIdAndOnlyTheFirstCloseIsRecorded()
    {
        var log = new Log();
        var buffer = new FoundgineMcpSseBuffer(5, 1000);
        buffer.Append("a");
        var stream = FoundgineMcpSseStream.Open(buffer, "1", new Recorder(), Inline, Options(null, 0, log)).Stream!;
        stream.Start();
        stream.Start();   // a second start neither re-opens nor re-audits
        stream.Close();
        stream.Close(FoundgineMcpSseCloseReason.Shutdown);
        Assert.Equal(new[] { FoundgineMcpAuditEventType.SseOpened, FoundgineMcpAuditEventType.SseClosed },
            log.Events.Select(e => e.Type).ToArray());
        Assert.Equal("resume=true", log.Events[0].Detail);
        Assert.Equal("reason=client_closed", log.Events[1].Detail);
        foreach (var e in log.Events)
        {
            Assert.Equal(FoundgineMcpSessionManager.Fingerprint(Session), e.Subject);
            Assert.Equal("alice", e.Actor);
            Assert.DoesNotContain(Session, e.Subject ?? "");
        }
    }

    [Fact]
    public void AThrowingAuditSinkNeverBreaksTheStream()
    {
        var opened = FoundgineMcpSseStream.Open(new FoundgineMcpSseBuffer(5, 1000), null, new Recorder(), Inline,
            new FoundgineMcpSseStreamOptions(Session, "alice", null, 0, new ThrowingLog(), null));
        Assert.Equal(FoundgineMcpSseOpenStatus.Ok, opened.Status);
        opened.Stream!.Start();
        opened.Stream.Close();
    }

    [Fact]
    public void LimitsValidateAndUnboundSessionsShareOneBucket()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FoundgineMcpSseLimits(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FoundgineMcpSseLimits(1, 0));
        var limits = new FoundgineMcpSseLimits(5, 1);
        var p = limits.TryAcquire(null);
        Assert.NotNull(p);
        Assert.Null(limits.TryAcquire(null));
        Assert.NotNull(limits.TryAcquire("bob"));
        p!.Dispose(); p.Dispose();   // idempotent: a double dispose must not free a second slot
        Assert.Equal(1, limits.Open);
        Assert.NotNull(limits.TryAcquire(null));
    }
}
