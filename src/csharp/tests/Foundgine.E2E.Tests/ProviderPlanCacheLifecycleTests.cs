using Foundgine.Core.Execution;
using Xunit;

namespace Foundgine.E2E.Tests;

public sealed class ProviderPlanCacheLifecycleTests
{
    [Fact]
    public void Namespace_invalidation_removes_only_the_retired_engine_entries()
    {
        var cache = new MemoryProviderPlanCache();
        cache.Set("engine-a:plan-1", new TestPlan("test"));
        cache.Set("engine-b:plan-1", new TestPlan("test"));

        cache.RemoveNamespace("engine-a");

        Assert.False(cache.TryGet("engine-a:plan-1", out _));
        Assert.True(cache.TryGet("engine-b:plan-1", out _));
        Assert.Throws<InvalidOperationException>(() =>
            cache.GetOrAdd("engine-a:plan-2", () => new TestPlan("test")));
    }

    [Fact]
    public async Task Compilation_started_before_invalidation_cannot_be_admitted_after_retirement()
    {
        var cache = new MemoryProviderPlanCache();
        using var compiling = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var task = Task.Run(() => cache.GetOrAdd("engine-a:slow", () =>
        {
            compiling.Set();
            release.Wait();
            return new TestPlan("test");
        }));

        Assert.True(compiling.Wait(TimeSpan.FromSeconds(5)));
        cache.RemoveNamespace("engine-a");
        release.Set();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await task;
        });
        Assert.False(cache.TryGet("engine-a:slow", out _));
    }

    private sealed record TestPlan(string Provider) : ProviderPlan(Provider);
}
