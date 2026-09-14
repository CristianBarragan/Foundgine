namespace Foundgine.Core.Execution;

/// <summary>
/// Compiles provider-neutral execution work into a provider-specific plan.
/// Providers consume ExecutionIR only; semantic planning artifacts never cross
/// this boundary.
/// </summary>
public interface IProviderPlanCompiler
{
    ProviderPlan Compile(ExecutionIR ir);
}
