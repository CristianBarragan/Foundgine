namespace Foundgine.Core.Execution;

/// <summary>
/// Cache for already-authorized provider plans. Cache keys are derived from the
/// complete provider-independent execution plan, including authorization
/// predicates. Runtime execution-context values are deliberately not part of
/// provider plans and are resolved by the provider at execution time.
/// </summary>
public interface IProviderPlanCache
{
    bool TryGet(string key, out ProviderPlan plan);

    void Set(string key, ProviderPlan plan);
}
