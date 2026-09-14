using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.IR.Graph;
using Foundgine.Core.Semantic.Planning.Algebra;
using Foundgine.Core.Semantic.Security.Execution;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Turns authorized Semantic IR into the canonical semantic planning
/// artifact. The resulting SemanticPlan is lowered to ExecutionIR separately. This planner does not discover relationships, resolve storage, or
/// choose a physical provider strategy; those concerns belong to earlier and
/// later layers respectively.
/// </summary>
public sealed class Planner : IPlanner
{
    public SemanticPlan Plan(SemanticContractSnapshot contract, SemanticOperation operation)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(operation);
        SemanticOperationContractValidator.Validate(operation, contract);
        return Plan(operation);
    }

    public SemanticPlan Plan(
        SemanticContractSnapshot contract,
        SemanticAuthorizationResult authorization)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(authorization);
        authorization.EnsureMatches(contract);
        SemanticOperationContractValidator.Validate(authorization.Operation, contract);
        return Plan(contract, authorization.Operation) with
        {
            AuthorizationBinding = SemanticPlanAuthorizationBinding.Create(contract, authorization.Evidence)
        };
    }

    public SemanticPlan Plan(SemanticOperationGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        SemanticOperationAlgebra.Validate(graph);
        SemanticOperationGraphSafetyValidator.Validate(graph, new SecurityResourceLimits());
        return Plan(graph.ToOperation());
    }

    public SemanticPlan Plan(SemanticOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var visited = new HashSet<int>();
        var root = BuildNode(operation.Root, visited, isRoot: true);
        return new SemanticPlan(root);
    }

    /// <summary>
    /// Compatibility adapter. New orchestration code should compile the graph
    /// to canonical Semantic IR before invoking the planner.
    /// </summary>
    public SemanticPlan Plan(SemanticGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return Plan(SemanticOperationCompiler.Compile(graph));
    }

    private static SemanticPlanNode BuildNode(
        SemanticReadNode semanticNode,
        ISet<int> visited,
        bool isRoot)
    {
        if (!visited.Add(semanticNode.Id))
            throw new InvalidOperationException(
                $"Semantic operation contains a cycle or duplicate node at {semanticNode.Id}.");

        if (!isRoot && semanticNode.ViaRelationship is null && semanticNode.ViaConnection is null)
            throw new InvalidOperationException(
                $"Non-root semantic node {semanticNode.Id} must specify the relationship or connection used to reach it.");

        if (semanticNode.ViaRelationship is not null && semanticNode.ViaConnection is not null)
            throw new InvalidOperationException(
                $"Semantic node {semanticNode.Id} cannot specify both a relationship and a connection.");

        if (isRoot && (semanticNode.ViaRelationship is not null || semanticNode.ViaConnection is not null))
            throw new InvalidOperationException(
                $"Root semantic node {semanticNode.Id} cannot specify a parent edge.");

        var operation = isRoot
            ? ExecutionOperation.Scan
            : semanticNode.ViaConnection is not null
                ? ExecutionOperation.TraverseConnection
                : ExecutionOperation.Traverse;

        var children = semanticNode.Children
            .Select(child => BuildNode(child, visited, isRoot: false))
            .ToArray();

        return new SemanticPlanNode(
            semanticNode.Id,
            operation,
            semanticNode.EntityId,
            semanticNode.Fields,
            semanticNode.ViaRelationship,
            semanticNode.ViaConnection,
            children,
            isRoot ? semanticNode.QueryOptions : null,
            semanticNode.Authorization);
    }
}
