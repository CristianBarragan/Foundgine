using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.IR.Graph;

namespace Foundgine.Core.Semantic.Planning;

public interface IPlanner
{
    SemanticPlan Plan(SemanticOperation operation);

    /// <summary>
    /// Plans an immutable semantic operation graph. The graph is converted back
    /// to canonical Semantic IR without mutation before the existing planning
    /// boundary is applied.
    /// </summary>
    SemanticPlan Plan(SemanticOperationGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return Plan(graph.ToOperation());
    }

    SemanticPlan Plan(SemanticGraph graph);

    /// <summary>
    /// Plans an operation against the trusted immutable semantic contract.
    /// The default implementation preserves compatibility for custom planners
    /// while enforcing contract membership before planning.
    /// </summary>
    SemanticPlan Plan(SemanticContractSnapshot contract, SemanticOperation operation)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(operation);
        SemanticOperationContractValidator.Validate(operation, contract);
        return Plan(operation);
    }

    /// <summary>
    /// Plans only an operation that has been authorized against the same
    /// immutable semantic contract. The resulting plan retains that binding.
    /// </summary>
    SemanticPlan Plan(
        SemanticContractSnapshot contract,
        SemanticAuthorizationResult authorization)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(authorization);
        authorization.EnsureMatches(contract);
        SemanticOperationContractValidator.Validate(authorization.Operation, contract);
        var plan = Plan(contract, authorization.Operation);
        return plan with
        {
            AuthorizationBinding = SemanticPlanAuthorizationBinding.Create(contract, authorization.Evidence)
        };
    }
}