namespace Foundgine.Providers.Tools.MCP;

/// <summary>
/// The SSE replay buffers of all sessions, one per session id. Obtain a buffer only for a session the
/// <see cref="FoundgineMcpSessionGate"/> has admitted. Bounded: at <c>maxStreams</c> the least-recently-used buffer is
/// dropped, so an abandoned session can never pin memory; its client then sees an invalid or evicted resume and
/// resynchronises. Whenever a buffer leaves the registry (evicted, <see cref="Remove"/>d or <see cref="Sweep"/>t) it is retired,
/// which closes any live stream attached to it. Call <see cref="Remove"/> when a session is terminated and <see cref="Sweep"/>
/// periodically so expired sessions do not keep buffers (the session store is keyed by digest, so only a liveness check can
/// tell). In-process only. Thread-safe.
/// </summary>
public sealed class FoundgineMcpSseRegistry
{
    private readonly int _maxStreams;
    private readonly int _maxEvents;
    private readonly long _maxBytes;
    private readonly Dictionary<string, LinkedListNode<(string Id, FoundgineMcpSseBuffer Buffer)>> _index = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Id, FoundgineMcpSseBuffer Buffer)> _lru = new();   // first = least recently used
    private readonly object _gate = new();

    public FoundgineMcpSseRegistry(int maxStreams = 10_000, int maxEvents = 256, long maxBytes = 1_048_576)
    {
        if (maxStreams <= 0) throw new ArgumentOutOfRangeException(nameof(maxStreams), "Maximum streams must be positive.");
        _ = new FoundgineMcpSseBuffer(maxEvents, maxBytes);   // validates the bounds
        _maxStreams = maxStreams;
        _maxEvents = maxEvents;
        _maxBytes = maxBytes;
    }

    /// <summary>The session's buffer, created on first use. Only call this for an admitted, well-formed session id.</summary>
    public FoundgineMcpSseBuffer Stream(string sessionId)
    {
        if (!FoundgineMcpSessionManager.IsWellFormed(sessionId)) throw new ArgumentException("Not a session id.", nameof(sessionId));
        FoundgineMcpSseBuffer? evicted = null;
        FoundgineMcpSseBuffer buffer;
        lock (_gate)
        {
            if (_index.TryGetValue(sessionId, out var node))
            {
                _lru.Remove(node);
                _lru.AddLast(node);
                return node.Value.Buffer;
            }
            if (_index.Count >= _maxStreams)
            {
                var eldest = _lru.First!;
                _lru.RemoveFirst();
                _index.Remove(eldest.Value.Id);
                evicted = eldest.Value.Buffer;
            }
            buffer = new FoundgineMcpSseBuffer(_maxEvents, _maxBytes);
            _index[sessionId] = _lru.AddLast((sessionId, buffer));
        }
        evicted?.Retire();   // outside the lock: it runs the stream's close
        return buffer;
    }

    /// <summary>The session's buffer if one exists (never creates one), or null. Marks it recently used.</summary>
    public FoundgineMcpSseBuffer? Find(string? sessionId)
    {
        if (sessionId is null) return null;
        lock (_gate)
        {
            if (!_index.TryGetValue(sessionId, out var node)) return null;
            _lru.Remove(node);
            _lru.AddLast(node);
            return node.Value.Buffer;
        }
    }

    /// <summary>Drops a session's buffer (call on termination). Returns whether one existed.</summary>
    public bool Remove(string sessionId)
    {
        FoundgineMcpSseBuffer buffer;
        lock (_gate)
        {
            if (!_index.Remove(sessionId, out var node)) return false;
            _lru.Remove(node);
            buffer = node.Value.Buffer;
        }
        buffer.Retire();
        return true;
    }

    /// <summary>
    /// Drops the buffers of sessions that are no longer live (e.g. <c>manager.IsLive</c>). The check runs outside the
    /// registry's lock, because it may call a shared store. Returns how many buffers were dropped.
    /// </summary>
    public int Sweep(Func<string, bool> isLive)
    {
        string[] ids;
        lock (_gate) ids = _index.Keys.ToArray();
        var dropped = 0;
        foreach (var id in ids) if (!isLive(id) && Remove(id)) dropped++;
        return dropped;
    }

    public int Count { get { lock (_gate) return _index.Count; } }
}
