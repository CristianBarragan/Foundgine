using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.IR;

namespace Foundgine.Core.Semantic.Authorization;

/// <summary>
/// Validates that an operation is rooted entirely in the trusted semantic
/// contract before authorization policy evaluation begins.
/// </summary>
internal static class SemanticAuthorizationContractValidator
{
    public static void Validate(SemanticContractSnapshot contract, SemanticOperation operation)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(operation);

        var visited = new HashSet<int>();
        ValidateNode(contract, operation.Root, visited, isRoot: true, parent: null);
    }

    private static void ValidateNode(
        SemanticContractSnapshot contract,
        SemanticReadNode node,
        ISet<int> visited,
        bool isRoot,
        SemanticEntity? parent)
    {
        if (!visited.Add(node.Id))
            throw new InvalidOperationException($"Semantic operation contains a cycle or duplicate node at {node.Id}.");

        if (!contract.TryGet(node.EntityId, out var entity))
            throw new InvalidOperationException(
                $"Semantic operation node {node.Id} references unknown entity '{node.EntityId}'.");

        if (isRoot && (node.ViaRelationship is not null || node.ViaConnection is not null))
            throw new InvalidOperationException($"Root semantic node {node.Id} cannot specify a parent edge.");

        if (!isRoot && node.ViaRelationship is null && node.ViaConnection is null)
            throw new InvalidOperationException(
                $"Non-root semantic node {node.Id} must specify the relationship or connection used to reach it.");

        if (node.ViaRelationship is { } relationshipId)
        {
            if (node.ViaConnection is not null)
                throw new InvalidOperationException(
                    $"Semantic node {node.Id} cannot specify both a relationship and a connection.");

            if (parent is not null)
            {
                var relationship = parent.Relationships.FirstOrDefault(x => x.Id == relationshipId)
                                   ?? throw new InvalidOperationException(
                                       $"Semantic operation node {node.Id} references relationship '{relationshipId}' not declared on '{parent.Name}'.");

                if (relationship.Target != node.EntityId)
                    throw new InvalidOperationException(
                        $"Semantic operation node {node.Id} targets '{node.EntityId}', but relationship '{relationship.Name}' targets '{relationship.Target}'.");
            }
        }

        foreach (var field in node.Fields.Concat(node.RequiredFields).Distinct())
        {
            if (field != entity.Identity.FieldId && entity.Fields.All(x => x.Id != field))
                throw new InvalidOperationException(
                    $"Semantic operation node {node.Id} selects unknown field '{field}' on '{entity.Name}'.");
        }

        foreach (var child in node.Children)
            ValidateNode(contract, child, visited, isRoot: false, parent: entity);
    }
}