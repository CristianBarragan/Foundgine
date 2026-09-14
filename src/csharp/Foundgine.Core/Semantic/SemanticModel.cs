using System.Collections.ObjectModel;
using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic;

/// <summary>
/// Immutable semantic topology produced by <see cref="SemanticModelBuilder"/>.
/// Once built, the model is safe to share across concurrent resolutions and can
/// be deterministically versioned/cached.
/// </summary>
public sealed class SemanticModel
{
    private readonly IReadOnlyDictionary<EntityId, SemanticEntity> _entities;
    private readonly IReadOnlyList<SemanticTraversal> _traversals;
    private readonly string _contractFingerprint;

    internal SemanticModel(
        IReadOnlyDictionary<EntityId, SemanticEntity> entities,
        IReadOnlyList<SemanticTraversal>? traversals = null,
        bool isFrozen = false)
    {
        _entities = FreezeEntities(entities);
        _traversals = FreezeTraversals(traversals);
        _contractFingerprint = SemanticModelFingerprint.Compute(this);
        IsFrozen = isFrozen;
    }

    /// <summary>
    /// Indicates that this semantic contract has crossed the explicit freeze
    /// boundary and is safe to hand to trusted planning/execution components.
    /// The underlying model is defensively immutable regardless; this flag is
    /// an architectural lifecycle marker that prevents accidental use of a
    /// merely constructed contract where a trusted snapshot is required.
    /// </summary>
    public bool IsFrozen { get; }

    /// <summary>
    /// Returns a frozen semantic contract. Freezing never changes semantic
    /// content, identities, or the contract fingerprint.
    /// </summary>
    public SemanticModel Freeze() =>
        IsFrozen ? this : new SemanticModel(_entities, _traversals, isFrozen: true);

    /// <summary>
    /// Throws unless this model has crossed the explicit freeze boundary.
    /// </summary>
    public void EnsureFrozen()
    {
        if (!IsFrozen)
            throw new InvalidOperationException(
                "The semantic model must be frozen before it can be used as a trusted semantic contract.");
    }

    /// <summary>
    /// Creates the immutable runtime contract snapshot. The model must already
    /// have crossed the explicit freeze boundary; snapshot creation never
    /// implicitly freezes a model.
    /// </summary>
    public SemanticContractSnapshot CreateSnapshot()
    {
        EnsureFrozen();
        return new SemanticContractSnapshot(this);
    }

    private static IReadOnlyDictionary<EntityId, SemanticEntity> FreezeEntities(
        IReadOnlyDictionary<EntityId, SemanticEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var copy = new Dictionary<EntityId, SemanticEntity>(entities.Count);
        foreach (var pair in entities)
        {
            var entity = pair.Value;
            var fields = entity.Fields
                .Select(FreezeField)
                .ToArray();
            var relationships = entity.Relationships
                .Select(FreezeRelationship)
                .ToArray();
            var aliases = entity.EffectiveAliases
                .Select(x => new SemanticAlias(x.Name, x.Weight))
                .ToArray();

            copy[pair.Key] = entity with
            {
                Fields = new ReadOnlyCollection<SemanticField>(fields),
                Relationships = new ReadOnlyCollection<SemanticRelationship>(relationships),
                Aliases = new ReadOnlyCollection<SemanticAlias>(aliases)
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

    private static IReadOnlyList<SemanticTraversal> FreezeTraversals(IReadOnlyList<SemanticTraversal>? traversals)
    {
        var copy = (traversals ?? [])
            .Select(x => new SemanticTraversal(
                x.Source,
                x.Name,
                x.Target,
                new ReadOnlyCollection<RelationshipId>(x.Path.ToArray())))
            .ToArray();

        return new ReadOnlyCollection<SemanticTraversal>(copy);
    }

    public IReadOnlyCollection<SemanticEntity> Entities => _entities.Values.ToArray();

    /// <summary>Logical open-intent traversals. These may span multiple relationships.</summary>
    public IReadOnlyList<SemanticTraversal> Traversals => _traversals;

    /// <summary>
    /// Canonical SHA-256 fingerprint of the semantic contract. The fingerprint
    /// is stable across declaration order and independent module composition.
    /// </summary>
    public string ContractFingerprint => _contractFingerprint;


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

    public bool TryGet(EntityId id, out SemanticEntity entity) =>
        _entities.TryGetValue(id, out entity!);

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

    public SemanticEntity Get(EntityId id) =>
        _entities.TryGetValue(id, out var entity)
            ? entity
            : throw new KeyNotFoundException($"Entity {id} has no semantic descriptor.");
}