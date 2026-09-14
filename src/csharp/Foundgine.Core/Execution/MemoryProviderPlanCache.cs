using System.Collections.Concurrent;
using System.Threading;

namespace Foundgine.Core.Execution;

/// <summary>
/// Small bounded in-memory provider-plan cache. The cache is intentionally
/// process-local and per Foundgine engine registration. A cache hit never skips
/// semantic resolution or authorization; it only skips provider compilation.
/// </summary>
public sealed class MemoryProviderPlanCache : IProviderPlanCache
{
    private readonly ConcurrentDictionary<string, Lazy<ProviderPlan>> _plans = new(StringComparer.Ordinal);
    private readonly int _capacity;

    public MemoryProviderPlanCache(int capacity = 256)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
    }

    public bool TryGet(string key, out ProviderPlan plan)
    {
        if (_plans.TryGetValue(key, out var lazy))
        {
            plan = lazy.Value;
            return true;
        }

        plan = null!;
        return false;
    }

    public ProviderPlan GetOrAdd(string key, Func<ProviderPlan> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        var lazy = _plans.GetOrAdd(
            key,
            _ => new Lazy<ProviderPlan>(factory, LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var plan = lazy.Value;
            TrimIfNeeded();
            return plan;
        }
        catch
        {
            _plans.TryRemove(key, out _);
            throw;
        }
    }

    public void Set(string key, ProviderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(plan);

        _plans[key] = new Lazy<ProviderPlan>(
            () => plan,
            LazyThreadSafetyMode.ExecutionAndPublication);

        TrimIfNeeded();
    }

    private void TrimIfNeeded()
    {
        if (_plans.Count <= _capacity)
            return;

        foreach (var candidate in _plans.Keys)
        {
            if (_plans.Count <= _capacity)
                break;

            _plans.TryRemove(candidate, out _);
        }
    }
}
