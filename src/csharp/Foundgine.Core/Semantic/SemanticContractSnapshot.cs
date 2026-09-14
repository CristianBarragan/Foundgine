using System.Collections.ObjectModel;
using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic;

/// <summary>
/// Immutable runtime representation of a validated and frozen semantic contract.
/// A snapshot deliberately separates trusted runtime consumption from the
/// <see cref="SemanticModel"/> construction/lifecycle representation.
/// </summary>
public sealed class SemanticContractSnapshot
{
    private readonly IReadOnlyDictionary<EntityId, SemanticEntity> _entities;
    private readonly IReadOnlyDictionary<EntityId, IReadOnlyDictionary<string, int>> _entityAliasWeights;

    private readonly IReadOnlyDictionary<EntityId, IReadOnlyDictionary<FieldId, IReadOnlyDictionary<string, int>>>
        _fieldAliasWeights;

    private readonly IReadOnlyDictionary<RelationshipId, IReadOnlyDictionary<string, int>> _relationshipAliasWeights;
    private readonly IReadOnlyList<SemanticTraversal> _traversals;

    internal SemanticContractSnapshot(SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.EnsureFrozen();

        _entities = FreezeEntities(model.Entities);
        _entityAliasWeights = BuildEntityAliasIndex(_entities.Values);
        _fieldAliasWeights = BuildFieldAliasIndex(_entities.Values);
        _relationshipAliasWeights = BuildRelationshipAliasIndex(_entities.Values);
        _traversals = FreezeTraversals(model.Traversals);
        ContractFingerprint = model.ContractFingerprint;
    }

    /// <summary>
    /// The canonical fingerprint of the frozen semantic contract represented by this snapshot.
    /// </summary>
    public string ContractFingerprint { get; }

    /// <summary>All semantic entities in the contract.</summary>
    public IReadOnlyCollection<SemanticEntity> Entities => _entities.Values.ToArray();

    /// <summary>Logical caller-facing traversals in the contract.</summary>
    public IReadOnlyList<SemanticTraversal> Traversals => _traversals;

    public bool TryGet(EntityId id, out SemanticEntity entity) =>
        _entities.TryGetValue(id, out entity!);

    public SemanticEntity Get(EntityId id) =>
        _entities.TryGetValue(id, out var entity)
            ? entity
            : throw new KeyNotFoundException($"Entity {id} has no semantic descriptor.");

    public bool TryResolveEntity(string name, out SemanticEntity entity)
    {
        entity = _entities.Values.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) ||
            x.EffectiveAliases.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))!;
        return entity is not null;
    }

    public SemanticEntity ResolveEntity(string name) =>
        TryResolveEntity(name, out var entity)
            ? entity
            : throw new KeyNotFoundException($"Semantic entity '{name}' is not defined.");

    public bool TryGetTraversal(EntityId source, string name, out SemanticTraversal traversal)
    {
        traversal = _traversals.FirstOrDefault(x =>
            x.Source == source && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))!;
        return traversal is not null;
    }

    public SemanticTraversal GetTraversal(EntityId source, string name) =>
        TryGetTraversal(source, name, out var traversal)
            ? traversal
            : throw new KeyNotFoundException(
                $"Semantic traversal '{name}' is not defined on entity '{Get(source).Name}'.");


    public bool TryGetAlias(EntityId entityId, string alias, out int weight) =>
        TryGetWeight(_entityAliasWeights, entityId, alias, out weight);

    public bool TryGetAlias(EntityId entityId, FieldId fieldId, string alias, out int weight)
    {
        if (_fieldAliasWeights.TryGetValue(entityId, out var fields))
            return TryGetWeight(fields, fieldId, alias, out weight);

        weight = default;
        return false;
    }

    public bool TryGetAlias(RelationshipId relationshipId, string alias, out int weight) =>
        TryGetWeight(_relationshipAliasWeights, relationshipId, alias, out weight);

    private static bool TryGetWeight<TKey>(
        IReadOnlyDictionary<TKey, IReadOnlyDictionary<string, int>> index,
        TKey key,
        string alias,
        out int weight)
        where TKey : notnull
    {
        if (index.TryGetValue(key, out var aliases) && aliases.TryGetValue(alias, out weight))
            return true;

        weight = default;
        return false;
    }

    private static IReadOnlyDictionary<EntityId, IReadOnlyDictionary<string, int>> BuildEntityAliasIndex(
        IEnumerable<SemanticEntity> entities) =>
        entities.ToDictionary(
            x => x.Id,
            x => BuildAliasMap(x.EffectiveAliases),
            EqualityComparer<EntityId>.Default);

    private static IReadOnlyDictionary<EntityId, IReadOnlyDictionary<FieldId, IReadOnlyDictionary<string, int>>>
        BuildFieldAliasIndex(
            IEnumerable<SemanticEntity> entities) =>
        entities.ToDictionary(
            e => e.Id,
            e => (IReadOnlyDictionary<FieldId, IReadOnlyDictionary<string, int>>)e.Fields
                .ToDictionary(f => f.Id, f => BuildAliasMap(f.EffectiveAliases)));

    private static IReadOnlyDictionary<RelationshipId, IReadOnlyDictionary<string, int>> BuildRelationshipAliasIndex(
        IEnumerable<SemanticEntity> entities) =>
        entities.SelectMany(x => x.Relationships)
            .ToDictionary(x => x.Id, x => BuildAliasMap(x.EffectiveAliases));

    private static IReadOnlyDictionary<string, int> BuildAliasMap(IEnumerable<SemanticAlias> aliases)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in aliases)
        {
            if (alias.Weight is not int weight)
                continue;

            if (!map.TryGetValue(alias.Name, out var existing) || weight > existing)
                map[alias.Name] = weight;
        }

        return new ReadOnlyDictionary<string, int>(map);
    }

    private static IReadOnlyDictionary<EntityId, SemanticEntity> FreezeEntities(
        IEnumerable<SemanticEntity> entities)
    {
        var copy = new Dictionary<EntityId, SemanticEntity>();
        foreach (var entity in entities)
        {
            copy[entity.Id] = entity with
            {
                Fields = new ReadOnlyCollection<SemanticField>(
                    entity.Fields.Select(FreezeField).ToArray()),
                Relationships = new ReadOnlyCollection<SemanticRelationship>(
                    entity.Relationships.Select(FreezeRelationship).ToArray()),
                Aliases = new ReadOnlyCollection<SemanticAlias>(
                    entity.EffectiveAliases.Select(x => new SemanticAlias(x.Name, x.Weight)).ToArray())
            };
        }

        return new ReadOnlyDictionary<EntityId, SemanticEntity>(copy);
    }

    private static SemanticField FreezeField(SemanticField field) => field with
    {
        Aliases = new ReadOnlyCollection<SemanticAlias>(
            field.EffectiveAliases.Select(x => new SemanticAlias(x.Name, x.Weight)).ToArray()),
        Constraints = new ReadOnlyCollection<SemanticConstraint>(
            field.EffectiveConstraints
                .Select(x => new SemanticConstraint(x.Kind, x.Value, x.Minimum, x.Maximum))
                .ToArray())
    };

    private static SemanticRelationship FreezeRelationship(SemanticRelationship relationship) => relationship with
    {
        Aliases = new ReadOnlyCollection<SemanticAlias>(
            relationship.EffectiveAliases.Select(x => new SemanticAlias(x.Name, x.Weight)).ToArray())
    };

    private static IReadOnlyList<SemanticTraversal> FreezeTraversals(
        IEnumerable<SemanticTraversal> traversals)
    {
        var copy = traversals
            .Select(x => new SemanticTraversal(
                x.Source,
                x.Name,
                x.Target,
                new ReadOnlyCollection<RelationshipId>(x.Path.ToArray())))
            .ToArray();

        return new ReadOnlyCollection<SemanticTraversal>(copy);
    }
}