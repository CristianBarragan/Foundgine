namespace Foundgine.Providers.Tools.MCP;

/// <summary>
/// Caps how many live SSE streams may be open at once: in total and per principal, so one client (or one stolen credential)
/// cannot hold unbounded connections. A principal is the authenticated name the session is bound to; sessions with no owner
/// share one bucket. <see cref="TryAcquire"/> returns a <see cref="Permit"/> that must be disposed when the stream ends, or
/// null when a limit is reached. Thread-safe.
/// </summary>
public sealed class FoundgineMcpSseLimits
{
    /// <summary>No limits at all.</summary>
    public static readonly FoundgineMcpSseLimits Unlimited = new(int.MaxValue, int.MaxValue);

    private const string Unbound = "\0unbound";

    /// <summary>One open stream's claim; <c>Dispose</c> is idempotent.</summary>
    public sealed class Permit : IDisposable
    {
        private readonly FoundgineMcpSseLimits _owner;
        private readonly string _key;
        private bool _released;
        internal Permit(FoundgineMcpSseLimits owner, string key) { _owner = owner; _key = key; }

        public void Dispose()
        {
            lock (_owner._gate)
            {
                if (_released) return;
                _released = true;
                _owner._total--;
                var left = _owner._perOwner[_key] - 1;
                if (left == 0) _owner._perOwner.Remove(_key); else _owner._perOwner[_key] = left;
            }
        }
    }

    private readonly int _maxTotal;
    private readonly int _maxPerOwner;
    private readonly Dictionary<string, int> _perOwner = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _total;

    public FoundgineMcpSseLimits(int maxTotal, int maxPerOwner)
    {
        if (maxTotal <= 0) throw new ArgumentOutOfRangeException(nameof(maxTotal), "Maximum open streams must be positive.");
        if (maxPerOwner <= 0) throw new ArgumentOutOfRangeException(nameof(maxPerOwner), "Maximum streams per principal must be positive.");
        _maxTotal = maxTotal;
        _maxPerOwner = maxPerOwner;
    }

    /// <summary>Returns a permit, or null when the total or the principal's limit is reached.</summary>
    public Permit? TryAcquire(string? owner)
    {
        var key = owner ?? Unbound;
        lock (_gate)
        {
            _perOwner.TryGetValue(key, out var mine);
            if (_total >= _maxTotal || mine >= _maxPerOwner) return null;
            _total++;
            _perOwner[key] = mine + 1;
            return new Permit(this, key);
        }
    }

    public int Open { get { lock (_gate) return _total; } }
}
