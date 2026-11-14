namespace Foundgine.Providers.Tools.MCP;

/// <summary>Where SSE frames go. <c>Write</c> may block; both methods are called from one thread at a time.</summary>
public interface IFoundgineMcpSseSink
{
    void Write(string frame);
    void Complete();
}

/// <summary>
/// <c>Gone</c>: the session's buffer was retired (the session ended) before or while the stream opened.
/// </summary>
public enum FoundgineMcpSseOpenStatus { Ok, Evicted, Invalid, Busy, Limit, Gone }

/// <summary>Why a stream ended, as recorded in the <c>SseClosed</c> audit event (<c>reason=&lt;lower-case name&gt;</c>).</summary>
public enum FoundgineMcpSseCloseReason { ClientClosed, WriteFailed, Evicted, SessionEnded, Lifetime, Shutdown }

/// <param name="SessionId">The admitted session (used only for the audit fingerprint).</param>
/// <param name="Owner">The authenticated principal, for the per-principal limit and the audit trail; may be null.</param>
/// <param name="Limits">Limits.</param>
/// <param name="MaxLifetimeMilliseconds">How long a stream may stay open; zero or less for no limit.</param>
/// <param name="Audit">Audit.</param>
/// <param name="Clock">Clock.</param>
public sealed record FoundgineMcpSseStreamOptions(
    string? SessionId = null, string? Owner = null, FoundgineMcpSseLimits? Limits = null, long MaxLifetimeMilliseconds = 0,
    IFoundgineMcpAuditSink? Audit = null, Func<long>? Clock = null)
{
    internal FoundgineMcpSseLimits EffectiveLimits => Limits ?? FoundgineMcpSseLimits.Unlimited;
    internal long Now() => (Clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))();
}

/// <param name="Status"></param>
/// <param name="Stream">Non-null only when <paramref name="Status"/> is <see>
///         <cref>FoundgineMcpSseOpenStatus.Ok</cref>
///     </see>
///     .</param>
public sealed record FoundgineMcpSseOpened(FoundgineMcpSseOpenStatus Status, FoundgineMcpSseStream? Stream);

/// <summary>
/// Transport-neutral live SSE connection over one session's <see cref="FoundgineMcpSseBuffer"/>; the same behaviour as the Java
/// <c>FoundgineMcpSseStream</c>. The transport supplies a sink and calls <see cref="Start"/>; replay after <c>Last-Event-ID</c> and
/// every later event flow through one path: the stream keeps a cursor (the id of the last event it wrote) and, whenever the
/// buffer nudges it, writes <c>ReplayAfter(cursor)</c>, so events arrive in order, exactly once, with no gap.
/// </summary>
/// <remarks>
/// <para><b>Limits.</b> One stream per session (<c>Busy</c>); <see cref="FoundgineMcpSseLimits"/> caps streams in total and per
/// principal (<c>Limit</c>); a stream older than <c>MaxLifetimeMilliseconds</c> is closed at its next heartbeat so the client
/// reconnects with <c>Last-Event-ID</c>. A client that falls so far behind that unsent events are evicted is closed rather than
/// skipped; a write failure closes the stream; retiring the buffer (session ended) closes it. Writes happen on the supplied
/// executor, never under the buffer's lock. <see cref="Close()"/> is idempotent and safe from any thread.</para>
/// <para><b>Audit.</b> <c>SseOpened</c>, <c>SseRejected</c> and <c>SseClosed</c> carry the session fingerprint and the principal,
/// never the session id, a payload or a <c>Last-Event-ID</c>.</para>
/// </remarks>
public sealed class FoundgineMcpSseStream : IDisposable
{
    private const string KeepAlive = ": keep-alive\n\n";

    private readonly FoundgineMcpSseBuffer _buffer;
    private readonly FoundgineMcpSseBuffer.Subscription _subscription;
    private readonly FoundgineMcpSseLimits.Permit _permit;
    private readonly IFoundgineMcpSseSink _sink;
    private readonly FoundgineMcpSseStreamOptions _options;
    private readonly long _openedAt;
    private readonly bool _resume;
    private readonly object _drainLock = new();
    private string _cursor;
    private bool _closed;
    private bool _started;

    private FoundgineMcpSseStream(FoundgineMcpSseBuffer buffer, FoundgineMcpSseBuffer.Subscription subscription,
        FoundgineMcpSseLimits.Permit permit, IFoundgineMcpSseSink sink, FoundgineMcpSseStreamOptions options, string cursor, bool resume)
    {
        _buffer = buffer;
        _subscription = subscription;
        _permit = permit;
        _sink = sink;
        _options = options;
        _cursor = cursor;
        _resume = resume;
        _openedAt = options.Now();
    }

    /// <summary>
    /// Claims a stream permit and the session's stream slot, and checks <paramref name="lastEventId"/>. Nothing is written until
    /// <see cref="Start"/>, so the transport can still answer with a plain error status.
    /// </summary>
    /// <param name="sink"></param>
    /// <param name="executor">Runs the writes that follow an append (for example <c>work => _ = Task.Run(work)</c>).</param>
    /// <param name="buffer"></param>
    /// <param name="lastEventId"></param>
    /// <param name="options"></param>
    public static FoundgineMcpSseOpened Open(FoundgineMcpSseBuffer buffer, string? lastEventId, IFoundgineMcpSseSink sink,
        Action<Action> executor, FoundgineMcpSseStreamOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(executor);
        options ??= new FoundgineMcpSseStreamOptions();
        var permit = options.EffectiveLimits.TryAcquire(options.Owner);
        if (permit is null) return Rejected(options, FoundgineMcpSseOpenStatus.Limit);
        FoundgineMcpSseStream? self = null;
        var subscription = buffer.Subscribe(
            () =>
            {
                var s = Volatile.Read(ref self);
                if (s is null) return;
                try { executor(s.Drain); } catch (Exception) { s.Close(FoundgineMcpSseCloseReason.Shutdown); }
            },
            () =>   // off the caller's thread: closing waits for any write in progress to finish
            {
                var s = Volatile.Read(ref self);
                if (s is null) return;
                try { executor(() => s.Close(FoundgineMcpSseCloseReason.SessionEnded)); }
                catch (Exception) { s.Close(FoundgineMcpSseCloseReason.SessionEnded); }
            });
        if (subscription is null)
        {
            permit.Dispose();
            return Rejected(options, buffer.IsRetired ? FoundgineMcpSseOpenStatus.Gone : FoundgineMcpSseOpenStatus.Busy);
        }
        var probe = buffer.ReplayAfter(lastEventId);
        if (probe.Status != FoundgineMcpSseReplayStatus.Ok)
        {
            subscription.Dispose();
            permit.Dispose();
            return Rejected(options, probe.Status == FoundgineMcpSseReplayStatus.Evicted
                ? FoundgineMcpSseOpenStatus.Evicted : FoundgineMcpSseOpenStatus.Invalid);
        }
        var none = lastEventId is null || lastEventId.All(c => c == ' ' || c == '\t');
        var cursor = none ? buffer.LastSequence.ToString(System.Globalization.CultureInfo.InvariantCulture) : lastEventId!;
        var stream = new FoundgineMcpSseStream(buffer, subscription, permit, sink, options, cursor, !none);
        Volatile.Write(ref self, stream);
        if (buffer.IsRetired)   // retired between claiming the slot and now: the retire callback saw no stream yet
        {
            subscription.Dispose();
            permit.Dispose();
            return Rejected(options, FoundgineMcpSseOpenStatus.Gone);
        }
        return new FoundgineMcpSseOpened(FoundgineMcpSseOpenStatus.Ok, stream);
    }

    private static FoundgineMcpSseOpened Rejected(FoundgineMcpSseStreamOptions options, FoundgineMcpSseOpenStatus status)
    {
        Emit(options, FoundgineMcpAuditEventType.SseRejected, "status=" + status.ToString().ToUpperInvariant());
        return new FoundgineMcpSseOpened(status, null);
    }

    private static void Emit(FoundgineMcpSseStreamOptions o, FoundgineMcpAuditEventType type, string detail) =>
        FoundgineMcpAudit.Emit(o.Audit, new FoundgineMcpAuditEvent(type, o.Now(),
            FoundgineMcpSessionManager.Fingerprint(o.SessionId), o.Owner, detail));

    /// <summary>Writes the replay (and anything already appended) on the calling thread; later events follow on the executor.</summary>
    public void Start()
    {
        lock (_drainLock)
        {
            if (_closed || _started) return;
            _started = true;
        }
        Emit(_options, FoundgineMcpAuditEventType.SseOpened, "resume=" + (_resume ? "true" : "false"));
        Drain();
    }

    /// <summary>Writes an SSE comment so idle proxies keep the connection open, and ends a stream past its maximum lifetime.</summary>
    public void Heartbeat()
    {
        lock (_drainLock)
        {
            if (_closed) return;
            if (_options.MaxLifetimeMilliseconds > 0 && _options.Now() - _openedAt >= _options.MaxLifetimeMilliseconds)
            {
                Close(FoundgineMcpSseCloseReason.Lifetime);
                return;
            }
            try { _sink.Write(KeepAlive); } catch (Exception) { Close(FoundgineMcpSseCloseReason.WriteFailed); }
        }
    }

    private void Drain()
    {
        lock (_drainLock)
        {
            if (_closed) return;
            var replay = _buffer.ReplayAfter(_cursor);
            if (replay.Status != FoundgineMcpSseReplayStatus.Ok) { Close(FoundgineMcpSseCloseReason.Evicted); return; }
            foreach (var e in replay.Events)
            {
                try { _sink.Write(FoundgineMcpSseBuffer.Format(e)); }
                catch (Exception) { Close(FoundgineMcpSseCloseReason.WriteFailed); return; }
                _cursor = e.Id;
            }
        }
    }

    public bool IsClosed { get { lock (_drainLock) return _closed; } }

    /// <summary>Closes the stream because the client went away (the transport's completion callback).</summary>
    public void Close() => Close(FoundgineMcpSseCloseReason.ClientClosed);

    public void Dispose() => Close();

    /// <summary>Closes the stream for the given reason; only the first close is recorded.</summary>
    public void Close(FoundgineMcpSseCloseReason reason)
    {
        lock (_drainLock)
        {
            if (_closed) return;
            _closed = true;
        }
        _subscription.Dispose();
        _permit.Dispose();
        try { _sink.Complete(); } catch (Exception) { /* already gone */ }
        Emit(_options, FoundgineMcpAuditEventType.SseClosed, "reason=" + ToSnake(reason));
    }

    private static string ToSnake(FoundgineMcpSseCloseReason r) => r switch
    {
        FoundgineMcpSseCloseReason.ClientClosed => "client_closed",
        FoundgineMcpSseCloseReason.WriteFailed => "write_failed",
        FoundgineMcpSseCloseReason.Evicted => "evicted",
        FoundgineMcpSseCloseReason.SessionEnded => "session_ended",
        FoundgineMcpSseCloseReason.Lifetime => "lifetime",
        _ => "shutdown",
    };
}
