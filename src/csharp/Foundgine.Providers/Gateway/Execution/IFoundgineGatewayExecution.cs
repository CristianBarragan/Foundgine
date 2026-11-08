using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Runtime;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Gateway.Execution;

/// <summary>
/// Gateway-facing execution facade. It accepts Foundgine's provider-neutral intent contracts
/// and delegates all planning, authorization, warrant verification and provider execution to
/// the existing runtime engines. The gateway is not an alternate execution engine.
/// </summary>
public interface IFoundgineGatewayExecution
{
    Task<ExecutionResult> ExecuteReadAsync(
        ReadIntent intent,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default);

    MutationDryRunResult DryRunMutation(SemanticMutationOperationGraph graph);

    MutationPlanApproval ApproveMutation(SemanticMutationOperationGraph graph);

    Task<MutationExecutionResult> ExecuteMutationAsync(
        SemanticMutationOperationGraph graph,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default);

    Task<MutationExecutionResult> ExecuteApprovedMutationAsync(
        MutationPlanApproval approval,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default);
}
