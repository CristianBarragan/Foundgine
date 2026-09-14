using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Proves that canonical semantic IR belongs to the trusted contract that is
/// being used for planning. This prevents a planner from accepting an IR tree
/// whose identities were not present in the frozen runtime contract.
/// </summary>
internal static class SemanticOperationContractValidator
{
    public static void Validate(SemanticOperation operation, SemanticContractSnapshot contract)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(contract);

        var visited = new HashSet<int>();
        ValidateNode(operation.Root, contract, visited, isRoot: true);
    }

    private static void ValidateNode(
        SemanticReadNode node,
        SemanticContractSnapshot contract,
        ISet<int> visited,
        bool isRoot)
    {
        if (!visited.Add(node.Id))
            throw new InvalidOperationException(
                $"Semantic operation contains a cycle or duplicate node at {node.Id}.");

        if (!contract.TryGet(node.EntityId, out var entity))
            throw new InvalidOperationException(
                $"Semantic operation node {node.Id} references unknown entity '{node.EntityId}'.");

        if (isRoot && (node.ViaRelationship is not null || node.ViaConnection is not null))
            throw new InvalidOperationException(
                $"Root semantic node {node.Id} cannot specify a parent edge.");

        if (!isRoot && node.ViaRelationship is null && node.ViaConnection is null)
            throw new InvalidOperationException(
                $"Non-root semantic node {node.Id} must specify the relationship or connection used to reach it.");

        if (node.ViaRelationship is { } relationshipId)
        {
            if (node.ViaConnection is not null)
                throw new InvalidOperationException(
                    $"Semantic node {node.Id} cannot specify both a relationship and a connection.");
        }

        foreach (var field in node.Fields.Concat(node.RequiredFields).Distinct())
        {
            if (field != entity.Identity.FieldId && entity.Fields.All(x => x.Id != field))
                throw new InvalidOperationException(
                    $"Semantic operation node {node.Id} selects unknown field '{field}' on '{entity.Name}'.");
        }

        if (isRoot && node.QueryOptions is not null)
        {
            ValidateFilter(node.QueryOptions.Filter, entity, contract);
            ValidateOrder(node.QueryOptions.EffectiveOrder, entity, contract);
        }

        foreach (var child in node.Children)
        {
            if (child.ViaRelationship is { } childRelationshipId)
            {
                var relationship = entity.Relationships.FirstOrDefault(x => x.Id == childRelationshipId);
                if (relationship is null)
                    throw new InvalidOperationException(
                        $"Semantic operation node {child.Id} references relationship '{childRelationshipId}' not declared on '{entity.Name}'.");
                if (relationship.Target != child.EntityId)
                    throw new InvalidOperationException(
                        $"Semantic operation node {child.Id} targets '{child.EntityId}', but relationship '{relationship.Name}' targets '{relationship.Target}'.");
            }

            ValidateNode(child, contract, visited, isRoot: false);
        }
    }

    private static void ValidateFilter(
        SemanticFilterExpression? filter,
        SemanticEntity entity,
        SemanticContractSnapshot contract)
    {
        if (filter is null) return;

        switch (filter)
        {
            case SemanticFieldFilter field:
                EnsureField(entity, field.Field, "filter");
                break;
            case SemanticRelationshipFilter relationship:
                var relation = EnsureRelationship(entity, relationship.Relationship);
                ValidateFilter(relationship.Predicate, contract.Get(relation.Target), contract);
                break;
            case SemanticAggregateFilter aggregate:
                var aggregateRelation = EnsureRelationship(entity, aggregate.Relationship);
                if (aggregate.Field is { } aggregateField)
                    EnsureField(contract.Get(aggregateRelation.Target), aggregateField, "aggregate filter");
                ValidateFilter(aggregate.Predicate, contract.Get(aggregateRelation.Target), contract);
                break;
            case SemanticAndFilter and:
                foreach (var expression in and.Expressions)
                    ValidateFilter(expression, entity, contract);
                break;
            case SemanticOrFilter or:
                foreach (var expression in or.Expressions)
                    ValidateFilter(expression, entity, contract);
                break;
            default:
                throw new InvalidOperationException($"Unsupported semantic filter '{filter.GetType().Name}'.");
        }
    }

    private static void ValidateOrder(
        IReadOnlyList<SemanticOrderTerm> order,
        SemanticEntity root,
        SemanticContractSnapshot contract)
    {
        foreach (var term in order)
        {
            var entity = root;
            foreach (var relationshipId in term.EffectivePath)
            {
                var relationship = EnsureRelationship(entity, relationshipId);
                entity = contract.Get(relationship.Target);
            }

            EnsureField(entity, term.Field, "order");
        }
    }

    private static SemanticRelationship EnsureRelationship(SemanticEntity entity, RelationshipId id) =>
        entity.Relationships.FirstOrDefault(x => x.Id == id)
        ?? throw new InvalidOperationException(
            $"Semantic operation references relationship '{id}' not declared on '{entity.Name}'.");

    private static void EnsureField(SemanticEntity entity, FieldId id, string context)
    {
        if (id != entity.Identity.FieldId && entity.Fields.All(x => x.Id != id))
            throw new InvalidOperationException(
                $"Semantic operation references unknown {context} field '{id}' on '{entity.Name}'.");
    }
}