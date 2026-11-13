using System.Text;
using System.Text.RegularExpressions;

namespace Foundgine.Providers.Tools.MCP;

/// <summary>One SSE event. <see cref="Id"/> is the decimal sequence number.</summary>
public sealed record FoundgineMcpSseEvent(long Sequence, string Data)
{
    public string Id => Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum FoundgineMcpSseReplayStatus { Ok, Evicted, Invalid }

public sealed record FoundgineMcpSseReplay(FoundgineMcpSseReplayStatus Status, IReadOnlyList<FoundgineMcpSseEvent> Events);

/// <summary>
/// Bounded, replayable event log for one MCP session's server-to-client SSE stream (spec <c>mcp-sse-replay-v1</c>).
/// </summary>
/// <remarks>
/// <para><b>Event ids</b> are the decimal sequence number, starting at 1 and never reused within a buffer. They carry no
/// authority: a buffer belongs to exactly one session (<see cref="FoundgineMcpSseRegistry"/>) and the transport must have
/// admitted the session (<see cref="FoundgineMcpSessionGate.AdmitGet"/>) before touching it.</para>
/// <para><b>Replay.</b> <see cref="ReplayAfter"/> returns every retained event after <c>Last-Event-ID</c>. A missing id yields
/// nothing. A non-numeric id, or one ahead of anything ever issued, is <see cref="FoundgineMcpSseReplayStatus.Invalid"/>. An id
/// older than the retained window is <see cref="FoundgineMcpSseReplayStatus.Evicted"/> (events were dropped; resynchronise).</para>
/// <para><b>Bounds.</b> At most <c>maxEvents</c> events and <c>maxBytes</c> of UTF-8 payload, oldest evicted first. A single
/// event larger than <c>maxBytes</c> is refused. Thread-safe.</para>
/// <para><b>One live stream.</b> <see cref="Subscribe"/> gives at most one wake-up callback at a time. The callback is only a
/// nudge, called after each successful append and outside this buffer's lock; the stream then reads the buffer itself with
/// <see cref="ReplayAfter"/>, so ordering and de-duplication come from the sequence numbers.</para>
/// </remarks>
public sealed class FoundgineMcpSseBuffer
{
    private readonly int _maxEvents;
    private readonly long _maxBytes;
    private readonly LinkedList<FoundgineMcpSseEvent> _events = new();
    private readonly object _gate = new();
    private long _bytes;
    private long _lastSequence;
    private Subscription? _active;
    private bool _retired;

    /// <summary>A claimed live-stream slot. <c>Dispose</c> releases it (idempotent; a stale handle never frees a newer one).</summary>
    public sealed class Subscription : IDisposable
    {
        private readonly FoundgineMcpSseBuffer _owner;
        internal readonly Action Wake;
        internal readonly Action OnRetire;
        internal Subscription(FoundgineMcpSseBuffer owner, Action wake, Action onRetire) { _owner = owner; Wake = wake; OnRetire = onRetire; }
        public void Dispose() { lock (_owner._gate) { if (ReferenceEquals(_owner._active, this)) _owner._active = null; } }
    }

    public FoundgineMcpSseBuffer(int maxEvents, long maxBytes)
    {
        if (maxEvents <= 0) throw new ArgumentOutOfRangeException(nameof(maxEvents), "Maximum events must be positive.");
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes), "Maximum bytes must be positive.");
        _maxEvents = maxEvents;
        _maxBytes = maxBytes;
    }

    /// <summary>Appends an event. Throws <see cref="ArgumentException"/> when it alone exceeds the byte bound.</summary>
    public FoundgineMcpSseEvent Append(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var size = Encoding.UTF8.GetByteCount(data);
        if (size > _maxBytes) throw new ArgumentException("The event is larger than the buffer's byte limit.", nameof(data));
        FoundgineMcpSseEvent e;
        Action? wake;
        lock (_gate)
        {
            e = new FoundgineMcpSseEvent(++_lastSequence, data);
            _events.AddLast(e);
            _bytes += size;
            while (_events.Count > _maxEvents || _bytes > _maxBytes)
            {
                var old = _events.First!.Value;
                _events.RemoveFirst();
                _bytes -= Encoding.UTF8.GetByteCount(old.Data);
            }
            wake = _active?.Wake;
        }
        try { wake?.Invoke(); } catch { /* a nudge must never fail an append */ }
        return e;
    }

    /// <summary>
    /// Claims the single live-stream slot. <paramref name="onAppend"/> must be cheap and non-blocking (hand the work to another
    /// thread); it is never called under this buffer's lock. <paramref name="onRetire"/> is called once, outside the lock, if the
    /// buffer is retired while this slot is held. Returns null when a stream is already attached or the buffer was retired.
    /// </summary>
    public Subscription? Subscribe(Action onAppend, Action onRetire)
    {
        ArgumentNullException.ThrowIfNull(onAppend);
        ArgumentNullException.ThrowIfNull(onRetire);
        lock (_gate)
        {
            if (_active is not null || _retired) return null;
            _active = new Subscription(this, onAppend, onRetire);
            return _active;
        }
    }

    /// <summary>Whether <see cref="Retire"/> has been called.</summary>
    public bool IsRetired { get { lock (_gate) return _retired; } }

    /// <summary>
    /// Ends the buffer's life (its session ended or it was dropped): the holder of the live-stream slot, if any, is told and the
    /// slot is freed for good. The retained events stay readable. Idempotent.
    /// </summary>
    public void Retire()
    {
        Subscription? holder;
        lock (_gate)
        {
            _retired = true;
            holder = _active;
            _active = null;
        }
        try { holder?.OnRetire(); } catch { /* the owner is gone anyway */ }
    }

    /// <summary>The sequence of the newest event ever appended, or 0.</summary>
    public long LastSequence { get { lock (_gate) return _lastSequence; } }

    public int Count { get { lock (_gate) return _events.Count; } }

    /// <param name="lastEventId">The raw <c>Last-Event-ID</c> header, or null/blank for none.</param>
    public FoundgineMcpSseReplay ReplayAfter(string? lastEventId)
    {
        var trimmed = lastEventId is null ? "" : lastEventId.Trim(' ', '\t');   // only space and tab, as in the Java runtime
        if (trimmed.Length == 0) return new(FoundgineMcpSseReplayStatus.Ok, Array.Empty<FoundgineMcpSseEvent>());
        var after = Parse(trimmed);
        lock (_gate)
        {
            if (after < 0 || after > _lastSequence) return new(FoundgineMcpSseReplayStatus.Invalid, Array.Empty<FoundgineMcpSseEvent>());
            var oldestRetained = _events.Count == 0 ? _lastSequence + 1 : _events.First!.Value.Sequence;
            if (after < oldestRetained - 1) return new(FoundgineMcpSseReplayStatus.Evicted, Array.Empty<FoundgineMcpSseEvent>());
            return new(FoundgineMcpSseReplayStatus.Ok, _events.Where(e => e.Sequence > after).ToList());
        }
    }

    private static long Parse(string s)
    {
        if (s.Length == 0 || s.Length > 18) return -1;
        foreach (var c in s) if (c < '0' || c > '9') return -1;
        return long.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One SSE frame: <c>id:</c>, one <c>data:</c> line per payload line (embedded newlines can never end the event early or
    /// inject fields), then the blank line. CRLF, LF and CR are all line breaks.
    /// </summary>
    public static string Format(FoundgineMcpSseEvent e)
    {
        var sb = new StringBuilder("id: ").Append(e.Id).Append('\n');
        foreach (var line in Regex.Split(e.Data, "\r\n|\n|\r")) sb.Append("data: ").Append(line).Append('\n');
        return sb.Append('\n').ToString();
    }

    public static string Format(IEnumerable<FoundgineMcpSseEvent> events) => string.Concat(events.Select(e => Format(e)));
}
