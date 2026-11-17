using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace Foundgine.Core.Execution;

/// <summary>
/// Small bounded in-memory provider-plan cache. The cache is intentionally
/// process-local and per Foundgine engine registration. A cache hit never skips
/// semantic resolution or authorization; it only skips provider compilation.
/// </summary>
/// <remarks>
/// <para>
/// The hit path takes no locks and does no admission or eviction work. The dictionary's <c>Count</c> and
/// <c>Keys</c> (which lock every bucket) are never used on a request.
/// </para>
/// <para>
/// Bounding invariant: <c>_order</c> and <c>_admitted</c> always hold exactly the same keys, and only keys whose
/// plan compiled successfully are ever in them. Both are touched only under <c>_gate</c>, on the miss path.
/// A failing compile therefore never leaves a queue slot behind (no unbounded growth, and no early eviction of a
/// later healthy entry stored under the same key). <see cref="Set"/> and <see cref="GetOrAdd"/> may race on the
/// same key; <c>_admitted.Add</c> decides, under the gate, which one takes the single queue slot.
/// Eviction is FIFO (oldest admitted first), not LRU: hits do not refresh an entry. Entries still compiling are
/// not counted until they succeed, so the dictionary can briefly exceed capacity by the number of in-flight compiles.
/// </para>
/// </remarks>
public sealed class MemoryProviderPlanCache : IProviderPlanCache, INamespacedProviderPlanCache
{
    private readonly ConcurrentDictionary<string, Lazy<ProviderPlan>> _plans = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _admitted = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _invalidatedNamespaces = new(StringComparer.Ordinal);
    private readonly int _capacity;

    public MemoryProviderPlanCache(int capacity = 256)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
    }

    public bool TryGet(string key, out ProviderPlan plan)
    {
        if (IsInvalidated(key))
        {
            plan = null!;
            return false;
        }

        if (_plans.TryGetValue(key, out var lazy))
        {
            try
            {
                plan = lazy.Value;
                if (IsInvalidated(key))
                {
                    plan = null!;
                    return false;
                }
                return true;
            }
            catch
            {
                // An in-flight compile that failed is not a cached plan. Its creator removes the entry.
            }
        }

        plan = null!;
        return false;
    }

    public ProviderPlan GetOrAdd(string key, Func<ProviderPlan> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        ThrowIfInvalidated(key);

        // Fast path: a hit does no allocation, no admission and no eviction.
        if (_plans.TryGetValue(key, out var existing))
        {
            var hit = Resolve(key, existing);
            ThrowIfInvalidated(key, existing);
            return hit;
        }

        var candidate = new Lazy<ProviderPlan>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
        var stored = _plans.GetOrAdd(key, candidate);

        // Resolve throws (and removes the entry) when the compile failed, so admission below runs only on success.
        var plan = Resolve(key, stored);
        ThrowIfInvalidated(key, stored);
        if (ReferenceEquals(stored, candidate))
            Admit(key, stored);
        ThrowIfInvalidated(key, stored);

        return plan;
    }

    public void Set(string key, ProviderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(plan);

        ThrowIfInvalidated(key);
        // Replacement and admission are one transaction with respect to FIFO eviction.
        // Otherwise an eviction can remove a newly replaced value between these operations.
        lock (_gate)
        {
            ThrowIfInvalidated(key);
            _plans[key] = new Lazy<ProviderPlan>(() => plan, LazyThreadSafetyMode.ExecutionAndPublication);
            AdmitUnderLock(key);
        }
    }

    private ProviderPlan Resolve(string key, Lazy<ProviderPlan> lazy)
    {
        try
        {
            return lazy.Value;
        }
        catch
        {
            // Remove only this failed entry, never a healthy one another caller has since stored.
            // A failed entry was never admitted, so there is no queue slot to release.
            _plans.TryRemove(new KeyValuePair<string, Lazy<ProviderPlan>>(key, lazy));
            throw;
        }
    }

    private void Admit(string key, Lazy<ProviderPlan> expected)
    {
        lock (_gate)
        {
            // The dictionary entry may have been replaced or evicted while the factory ran.
            // Never create an admission record for a value that is no longer stored.
            if (!_plans.TryGetValue(key, out var current) || !ReferenceEquals(current, expected))
                return;

            AdmitUnderLock(key);
        }
    }

    private void AdmitUnderLock(string key)
    {
        ThrowIfInvalidated(key);
        // Already holds a slot (a concurrent Set/GetOrAdd for the same key got here first): nothing to add.
        if (!_admitted.Add(key))
            return;

        _order.Enqueue(key);
        while (_order.Count > _capacity)
        {
            var oldest = _order.Dequeue();
            _admitted.Remove(oldest);
            _plans.TryRemove(oldest, out _);
        }
    }

    /// <summary>
    /// Permanently invalidates one engine-generation namespace and removes its entries.
    /// Call only after the owning engine has stopped accepting requests and drained in-flight work.
    /// Replay and warrant-revocation stores are deliberately outside this cache and are never purged.
    /// </summary>
    public void RemoveNamespace(string cacheNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheNamespace);
        _invalidatedNamespaces.TryAdd(cacheNamespace, 0);
        var prefix = cacheNamespace + ":";

        lock (_gate)
        {
            foreach (var key in _plans.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                _plans.TryRemove(key, out _);
                _admitted.Remove(key);
            }

            if (_order.Count > 0)
            {
                var retained = _order.Where(key => !key.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                _order.Clear();
                foreach (var key in retained)
                    _order.Enqueue(key);
            }
        }
    }

    private bool IsInvalidated(string key)
    {
        var separator = key.IndexOf(':');
        return separator > 0 && _invalidatedNamespaces.ContainsKey(key[..separator]);
    }

    private void ThrowIfInvalidated(string key, Lazy<ProviderPlan>? expected = null)
    {
        if (!IsInvalidated(key))
            return;

        // A GetOrAdd that passed its initial check can race namespace retirement and
        // insert after the purge's dictionary scan. Remove only the exact entry it saw.
        if (expected is not null)
            _plans.TryRemove(new KeyValuePair<string, Lazy<ProviderPlan>>(key, expected));

        throw new InvalidOperationException("The provider-plan cache namespace has been invalidated.");
    }
}
