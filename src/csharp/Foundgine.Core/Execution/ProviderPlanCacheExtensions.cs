namespace Foundgine.Core.Execution;

public static class ProviderPlanCacheExtensions
{
    /// <summary>
    /// Gets an existing provider plan or creates it once. The built-in memory
    /// cache uses single-flight compilation so concurrent requests for the same
    /// uncached key do not stampede the provider compiler.
    /// </summary>
    public static ProviderPlan GetOrAdd(
        this IProviderPlanCache cache,
        string key,
        Func<ProviderPlan> factory)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (cache is MemoryProviderPlanCache memory)
            return memory.GetOrAdd(key, factory);

        if (cache.TryGet(key, out var existing))
            return existing;

        var created = factory();
        cache.Set(key, created);
        return created;
    }
}
