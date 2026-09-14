namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Optimizes an already-authorized semantic plan without changing its domain
/// meaning or introducing provider-specific instructions.
/// </summary>
public interface IPlanOptimizer
{
    SemanticPlanOptimizationResult Optimize(SemanticPlan plan);
}