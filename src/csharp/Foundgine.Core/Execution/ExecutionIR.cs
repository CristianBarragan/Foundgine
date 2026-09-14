using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Execution;

/// <summary>
/// Canonical provider-neutral execution representation.
///
/// Semantic IR answers what the operation means. Execution IR answers what
/// provider-neutral work must be performed. It deliberately contains no SQL,
/// storage names, provider types, aliases, or connection details.
/// </summary>
public sealed record ExecutionIR(
    ExecutionIRNode Root,
    IReadOnlyList<string> RequiredSecurityInvariants,
    SemanticPlanAuthorizationBinding AuthorizationBinding)
{
    public static ExecutionIR From(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var binding = plan.AuthorizationBinding
                      ?? throw new InvalidOperationException(
                          "An executable plan must carry authorization provenance.");

        return new ExecutionIR(
            ExecutionIRNode.From(plan.Root),
            plan.RequiredSecurityInvariants ?? Array.Empty<string>(),
            binding);
    }
}

public sealed record ExecutionIRNode(
    int Id,
    ExecutionOperation Operation,
    EntityId EntityId,
    IReadOnlyList<FieldId> Fields,
    RelationshipId? ViaRelationship,
    ConnectionId? ViaConnection,
    IReadOnlyList<ExecutionIRNode> Children,
    SemanticQueryOptions? QueryOptions = null,
    AuthorizationPredicate? Authorization = null,
    AggregateExecutionStrategy AggregateExecutionStrategy = AggregateExecutionStrategy.Default)
{
    internal static ExecutionIRNode From(SemanticPlanNode node) =>
        new(
            node.Id,
            node.Operation,
            node.EntityId,
            node.Fields,
            node.ViaRelationship,
            node.ViaConnection,
            node.Children.Select(From).ToArray(),
            node.QueryOptions,
            node.Authorization,
            node.AggregateExecutionStrategy);
}

/// <summary>
/// Explicit lowering boundary from the planner's semantic plan to the
/// provider-neutral execution representation.
/// </summary>
public static class ExecutionIRCompiler
{
    public static ExecutionIR Compile(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.AuthorizationBinding is null)
            throw new InvalidOperationException(
                "An executable plan must carry authorization provenance before crossing the execution boundary.");

        return ExecutionIR.From(plan);
    }

    public static ExecutionIR Compile(
        SemanticContractSnapshot contract,
        SemanticPlan plan,
        SemanticAuthorizationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evidence);

        if (plan.AuthorizationBinding is null)
            throw new InvalidOperationException(
                "The semantic plan has no authorization binding.");

        plan.AuthorizationBinding.EnsureMatches(contract, evidence);

        return Compile(plan);
    }
}