using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

/// <summary>
/// The contract every <see cref="IFoundgineMcpSessionStore"/> must meet, runnable against any implementation via
/// <see cref="Verify"/>. A clustered host implementing a shared store (Redis, SQL, ...) calls it from its own tests. The
/// second case is a store that, like a remote one, shares nothing by reference.
/// </summary>
public sealed class McpSessionStoreContractTests
{
    internal static void Verify(Func<IFoundgineMcpSessionStore> factory)
    {
        var store = factory();
        var s = new FoundgineMcpStoredSession("k1", null, 100, 100, 500, 900);
        Assert.True(store.TryAdd(s));
        Assert.False(store.TryAdd(new FoundgineMcpStoredSession("k1", "other", 1, 1, 2, 3)));   // a collision must not overwrite
        Assert.Equal(s, store.Find("k1"));
        Assert.Null(store.Find("missing"));
        Assert.Equal(1, store.Count);

        var touched = store.Touch("k1", 300, 700)!;
        Assert.Equal(300, touched.LastAccessedAtUnixMilliseconds);
        Assert.Equal(700, touched.IdleExpiresAtUnixMilliseconds);
        Assert.Equal(900, touched.AbsoluteExpiresAtUnixMilliseconds);
        Assert.Equal(100, touched.CreatedAtUnixMilliseconds);
        var backwards = store.Touch("k1", 50, 400)!;   // never moves deadlines or access time backwards
        Assert.Equal(300, backwards.LastAccessedAtUnixMilliseconds);
        Assert.Equal(700, backwards.IdleExpiresAtUnixMilliseconds);
        Assert.Null(store.Touch("missing", 1, 2));
        Assert.Null(store.Find("missing"));   // touch must never create a session

        store.TryAdd(new FoundgineMcpStoredSession("k2", null, 100, 100, 200, 900));
        store.TryAdd(new FoundgineMcpStoredSession("k3", null, 100, 100, 5_000, 800));
        Assert.Equal(1, store.RemoveExpired(200));   // earlier of idle/absolute deadline, inclusive
        Assert.Equal(0, store.RemoveExpired(200));
        Assert.Equal(2, store.RemoveExpired(800));
        Assert.Equal(0, store.Count);
        Assert.False(store.Remove("k1"));
        store.TryAdd(s);
        Assert.True(store.Remove("k1"));
        Assert.False(store.Remove("k1"));
    }

    [Fact] public void InMemoryStoreMeetsTheContract() => Verify(() => new InMemoryFoundgineMcpSessionStore());

    private sealed class CopyingStore : IFoundgineMcpSessionStore
    {
        private readonly Dictionary<string, FoundgineMcpStoredSession> _data = new();
        private readonly object _lock = new();
        private static FoundgineMcpStoredSession? Copy(FoundgineMcpStoredSession? s) => s is null ? null : s with { };
        public bool TryAdd(FoundgineMcpStoredSession session) { lock (_lock) return _data.TryAdd(session.Key, session with { }); }
        public FoundgineMcpStoredSession? Find(string key) { lock (_lock) return Copy(_data.GetValueOrDefault(key)); }
        public FoundgineMcpStoredSession? Touch(string key, long accessed, long idleExpires)
        {
            lock (_lock)
            {
                if (!_data.TryGetValue(key, out var s)) return null;
                var u = s with
                {
                    LastAccessedAtUnixMilliseconds = Math.Max(accessed, s.LastAccessedAtUnixMilliseconds),
                    IdleExpiresAtUnixMilliseconds = Math.Max(idleExpires, s.IdleExpiresAtUnixMilliseconds)
                };
                _data[key] = u;
                return Copy(u);
            }
        }
        public bool Remove(string key) { lock (_lock) return _data.Remove(key); }
        public int RemoveExpired(long now)
        {
            lock (_lock)
            {
                var dead = _data.Where(kv => kv.Value.ExpiresAtUnixMilliseconds <= now).Select(kv => kv.Key).ToList();
                foreach (var k in dead) _data.Remove(k);
                return dead.Count;
            }
        }
        public int Count { get { lock (_lock) return _data.Count; } }
    }

    [Fact]
    public void ACopyingStoreMeetsTheContractAndDrivesTheManager()
    {
        Verify(() => new CopyingStore());
        long now = 1_000;
        var m = new FoundgineMcpSessionManager(
            new FoundgineMcpSessionOptions(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(300), 5), new CopyingStore(), () => now);
        var s = m.Create("alice");
        Assert.True(m.Validate(s.SessionId, "alice").IsValid);
        Assert.False(m.Validate(s.SessionId, "bob").IsValid);
        now += 99;   // just inside the 100 ms idle window
        Assert.True(m.Validate(s.SessionId, "alice").IsValid);
        Assert.True(m.Terminate(s.SessionId, "alice"));
        Assert.False(m.Validate(s.SessionId, "alice").IsValid);
    }
}
