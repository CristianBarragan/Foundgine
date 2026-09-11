using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Mutation;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;
using Foundgine.Core.Semantic.Security.Execution;

namespace Foundgine.Runtime;

public interface IFoundgineMutations
{
    MutationDryRunResult DryRun(SemanticMutationRequest request);

    Task<MutationExecutionResult> ExecuteAsync(
        SemanticMutationRequest request,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default);

    MutationPlanApproval Approve(SemanticMutationRequest request, string approvedBy);

    Task<MutationExecutionResult> ExecuteApprovedAsync(
        MutationPlanApproval approval,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default);
}

public sealed record SemanticMutationRequest(
    SemanticMutationOperationGraph Graph,
    SecurityExecutionContext? Security = null);

public sealed record MutationDryRunResult(
    string PlanFingerprint,
    IReadOnlyList<MutationPlanOperation> Operations,
    IReadOnlyList<string> Effects);

public sealed record MutationPlanOperation(
    int Index,
    string Entity,
    string Kind,
    IReadOnlyList<string> Fields,
    IReadOnlyList<string> ReturnFields);

public sealed record MutationPlanApproval(
    SemanticMutationRequest Request,
    string ApprovalId,
    string PlanFingerprint,
    string ApprovedBy,
    DateTimeOffset ApprovedAt);

public sealed record MutationExecutionResult(
    MutationBatchResult Result,
    string PlanFingerprint,
    string ResultFingerprint,
    string? ApprovalId,
    string? ApprovedBy);