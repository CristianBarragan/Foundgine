using Xunit;

namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>Replay stores must reject a second consume yet stay bounded: entries are dropped once the warrant has expired.</summary>
public sealed class SecurityWarrantReplayStoreRetentionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = T0;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class LegacyStore : ISecurityWarrantReplayStore
    {
        private readonly HashSet<string> _used = [];

        public bool TryConsume(string warrantId, string nonce) => _used.Add(warrantId + nonce);
    }

    [Fact]
    public void Memory_store_rejects_replay_with_and_without_expiry()
    {
        var store = new MemorySecurityWarrantReplayStore();

        Assert.True(store.TryConsume("w", "n1"));
        Assert.False(store.TryConsume("w", "n1"));
        Assert.True(store.TryConsume("w", "n2", T0.AddSeconds(300)));
        Assert.False(store.TryConsume("w", "n2", T0.AddSeconds(300)));
        Assert.False(store.TryConsume("w", "n2"));
    }

    [Fact]
    public void Memory_store_drops_expired_entries_but_keeps_live_ones()
    {
        var time = new MutableTimeProvider();
        var store = new MemorySecurityWarrantReplayStore(time);
        for (var i = 0; i < 400; i++)
            Assert.True(store.TryConsume("w" + i, "n", T0.AddSeconds(300)));
        Assert.True(store.TryConsume("long-lived", "n", T0.AddSeconds(3600)));
        Assert.True(store.TryConsume("no-expiry", "n"));

        time.Advance(TimeSpan.FromMinutes(7)); // past expiry plus the skew grace
        for (var i = 0; i < 300; i++)
            Assert.True(store.TryConsume("fresh" + i, "n", T0.AddHours(2)));

        Assert.True(store.Count < 400, $"expired entries must be evicted, count={store.Count}");
        Assert.False(store.TryConsume("long-lived", "n", T0.AddSeconds(3600)));
        Assert.False(store.TryConsume("no-expiry", "n"));
    }

    [Fact]
    public void Entry_is_retained_through_the_skew_grace_after_expiry()
    {
        var time = new MutableTimeProvider();
        var store = new MemorySecurityWarrantReplayStore(time);
        Assert.True(store.TryConsume("w", "n", T0.AddSeconds(60)));

        time.Advance(TimeSpan.FromSeconds(61)); // expired, inside the grace window
        for (var i = 0; i < 300; i++)
            store.TryConsume("x" + i, "n", T0.AddHours(1));

        Assert.False(store.TryConsume("w", "n", T0.AddSeconds(60)));
    }

    [Fact]
    public void Legacy_store_implementing_only_the_two_argument_form_still_works_through_the_interface()
    {
        ISecurityWarrantReplayStore legacy = new LegacyStore();

        Assert.True(legacy.TryConsume("w", "n", T0));
        Assert.False(legacy.TryConsume("w", "n", T0));
    }

    [Fact]
    public void File_store_rejects_replay_across_instances()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "replay.log");
            var a = new FileSecurityWarrantReplayStore(path);
            var b = new FileSecurityWarrantReplayStore(path);
            var farFuture = DateTimeOffset.UtcNow.AddDays(1);

            Assert.True(a.TryConsume("w", "n", farFuture));
            Assert.False(b.TryConsume("w", "n", farFuture));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void File_store_keeps_legacy_lines_without_a_lifetime()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "replay.log");
            var store = new FileSecurityWarrantReplayStore(path);

            Assert.True(store.TryConsume("old", "n"));
            Assert.False(store.TryConsume("old", "n", T0.AddSeconds(60)));
            Assert.DoesNotContain('\t', File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void File_store_compacts_expired_entries_and_keeps_live_ones()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "replay.log");
            var time = new MutableTimeProvider();
            var store = new FileSecurityWarrantReplayStore(path, time);
            for (var i = 0; i < 20; i++)
                Assert.True(store.TryConsume("short" + i, "n", T0.AddSeconds(60)));
            Assert.True(store.TryConsume("live", "n", T0.AddHours(1)));
            Assert.Equal(21, File.ReadAllLines(path).Count(l => l.Length > 0));

            time.Advance(TimeSpan.FromMinutes(3));
            Assert.True(store.TryConsume("next", "n", T0.AddHours(2)));

            Assert.Equal(2, File.ReadAllLines(path).Count(l => l.Length > 0));
            Assert.False(store.TryConsume("live", "n", T0.AddHours(1)));
            Assert.False(store.TryConsume("next", "n", T0.AddHours(2)));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
