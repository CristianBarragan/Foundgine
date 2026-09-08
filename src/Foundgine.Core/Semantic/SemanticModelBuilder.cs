using System.Linq.Expressions;
using System.Reflection;
using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic;

/// <summary>
/// Builds the static semantic topology used by resolution. This builder
/// contains only domain concepts; storage/provider metadata is deliberately
/// outside this layer.
/// </summary>
public sealed class SemanticModelBuilder
{
    private readonly Dictionary<EntityId, SemanticEntity> _entities = new();
    private readonly List<SemanticTraversal> _traversals = [];

    /// <summary>
    /// Registers a semantic entity whose fields can be authored against the
    /// application/domain model type. Property selectors inside <paramref name="configure"/>
    /// target <typeparamref name="TModel"/>, not the semantic entity builder or provider metadata.
    /// </summary>
    public SemanticModelBuilder Entity<TModel>(
        EntityId id,
        string name,
        Action<SemanticEntityBuilder<TModel>> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        if (_entities.ContainsKey(id))
            throw new InvalidOperationException($"Semantic entity '{id}' is already registered.");

        var builder = new SemanticEntityBuilder<TModel>(id, name);
        configure(builder);
        var entity = builder.Build();
        ValidateEntityAliases(entity);
        _entities.Add(id, entity);
        return this;
    }

    /// <summary>
    /// Declares a relationship with explicit domain model types and property
    /// selectors on both sides. The generic arguments are the source and target
    /// application/domain models; the selectors therefore cannot accidentally
    /// reference the wrong model, semantic metadata, or provider entity type.
    /// </summary>
    /// <summary>Declares a relationship with a deterministic identity derived from its source entity and name.</summary>
    public SemanticModelBuilder Relationship<TFromModel, TToModel>(
        EntityId fromEntity,
        string name,
        Expression<Func<TFromModel, object?>> fromProperty,
        EntityId toEntity,
        Expression<Func<TToModel, object?>> toProperty,
        RelationshipCardinality cardinality)
    {
        var source = _entities.TryGetValue(fromEntity, out var entity) ? entity : null;
        if (source is null)
            throw new InvalidOperationException(
                $"Source semantic entity '{fromEntity}' must be registered before declaring relationship '{name}'.");

        return Relationship(fromEntity, RelationshipId.Create(source.Name, name), name, fromProperty, toEntity,
            toProperty, cardinality);
    }

    public SemanticModelBuilder Relationship<TFromModel, TToModel>(
        EntityId fromEntity,
        RelationshipId id,
        string name,
        Expression<Func<TFromModel, object?>> fromProperty,
        EntityId toEntity,
        Expression<Func<TToModel, object?>> toProperty,
        RelationshipCardinality cardinality)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_entities.TryGetValue(fromEntity, out var source))
            throw new InvalidOperationException(
                $"Source semantic entity '{fromEntity}' must be registered before declaring relationship '{name}'.");

        if (!_entities.ContainsKey(toEntity))
            throw new InvalidOperationException(
                $"Target semantic entity '{toEntity}' must be registered before declaring relationship '{name}'.");

        var from = GetProperty(fromProperty, typeof(TFromModel));
        var to = GetProperty(toProperty, typeof(TToModel));

        if (from.PropertyType != to.PropertyType)
        {
            throw new ArgumentException(
                $"Relationship '{source.Name}.{name}' maps '{typeof(TFromModel).Name}.{from.Name}' ({from.PropertyType.Name}) to '{typeof(TToModel).Name}.{to.Name}' ({to.PropertyType.Name}); both properties must have the same CLR type.");
        }

        var relationships = source.Relationships.ToList();
        relationships.Add(new SemanticRelationship(id, name, toEntity, cardinality));
        _entities[fromEntity] = source with { Relationships = relationships.ToArray() };
        return this;
    }

    /// <summary>
    /// Overlays an independently authored semantic model onto an existing model.
    /// Entities are matched by semantic name rather than by numeric identity, so
    /// a curated semantic declaration can enrich metadata discovered from a
    /// producer without copying the producer's structural identities.
    ///
    /// Existing structural fields and relationships are retained. When the
    /// overlay declares a field or relationship with the same semantic name,
    /// the existing identity and structural target are retained while aliases,
    /// constraints and explicitly authored capabilities are applied from the
    /// overlay. This is the composition point for metadata-first applications
    /// that want a small amount of strongly typed semantic authoring.
    /// </summary>
    public SemanticModelBuilder Overlay(SemanticModel overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        foreach (var overlayEntity in overlay.Entities)
        {
            var existing = _entities.Values.FirstOrDefault(entity =>
                string.Equals(entity.Name, overlayEntity.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                _entities.Add(overlayEntity.Id, overlayEntity);
                continue;
            }

            if (existing.ModelType is not null &&
                overlayEntity.ModelType is not null &&
                existing.ModelType != overlayEntity.ModelType)
            {
                throw new InvalidOperationException(
                    $"Semantic entity '{existing.Name}' is being overlaid by incompatible CLR model types '{existing.ModelType.FullName}' and '{overlayEntity.ModelType.FullName}'.");
            }

            var fields = existing.Fields.ToList();
            foreach (var overlayField in overlayEntity.Fields)
            {
                var index = fields.FindIndex(field =>
                    string.Equals(field.Name, overlayField.Name, StringComparison.OrdinalIgnoreCase));

                if (index < 0)
                {
                    fields.Add(overlayField);
                    continue;
                }

                var current = fields[index];
                if (current.ClrType != overlayField.ClrType)
                {
                    throw new InvalidOperationException(
                        $"Semantic field '{existing.Name}.{current.Name}' is being overlaid with incompatible CLR types '{current.ClrType.FullName}' and '{overlayField.ClrType.FullName}'.");
                }

                fields[index] = current with
                {
                    SemanticType = overlayField.SemanticType ?? current.SemanticType,
                    Capabilities = overlayField.Capabilities,
                    Aliases = MergeAliases(current.EffectiveAliases, overlayField.EffectiveAliases),
                    Constraints = MergeConstraints(current.EffectiveConstraints, overlayField.EffectiveConstraints),
                    NullableOverride = overlayField.NullableOverride ?? current.NullableOverride
                };
            }

            var relationships = existing.Relationships.ToList();
            foreach (var overlayRelationship in overlayEntity.Relationships)
            {
                var index = relationships.FindIndex(relationship =>
                    string.Equals(relationship.Name, overlayRelationship.Name, StringComparison.OrdinalIgnoreCase));

                if (index < 0)
                {
                    relationships.Add(overlayRelationship);
                    continue;
                }

                var current = relationships[index];
                var currentTargetName = _entities[current.Target].Name;
                var overlayTargetName = overlay.TryGet(overlayRelationship.Target, out var overlayTarget)
                    ? overlayTarget.Name
                    : overlayRelationship.Target.ToString();

                if (!string.Equals(currentTargetName, overlayTargetName, StringComparison.OrdinalIgnoreCase) ||
                    current.Cardinality != overlayRelationship.Cardinality)
                {
                    throw new InvalidOperationException(
                        $"Semantic relationship '{existing.Name}.{current.Name}' conflicts with the existing target/cardinality.");
                }

                relationships[index] = current with
                {
                    Aliases = MergeAliases(current.EffectiveAliases, overlayRelationship.EffectiveAliases)
                };
            }

            var identity = existing.Identity;
            if (!string.Equals(identity.Name, overlayEntity.Identity.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Semantic entity '{existing.Name}' has conflicting identities '{identity.Name}' and '{overlayEntity.Identity.Name}'.");
            }

            _entities[existing.Id] = existing with
            {
                Identity = identity,
                Fields = fields.ToArray(),
                Relationships = relationships.ToArray(),
                Aliases = MergeAliases(existing.EffectiveAliases, overlayEntity.EffectiveAliases),
                ModelType = existing.ModelType ?? overlayEntity.ModelType
            };
        }

        // A curated module may also contribute logical traversals. Those are
        // already semantic identities, so preserve them exactly when they do
        // not duplicate an existing traversal.
        foreach (var traversal in overlay.Traversals)
        {
            if (!_traversals.Any(existingTraversal =>
                    existingTraversal.Source == traversal.Source &&
                    string.Equals(existingTraversal.Name, traversal.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _traversals.Add(traversal);
            }
        }

        return this;
    }

    private static IReadOnlyList<SemanticAlias> MergeAliases(
        IEnumerable<SemanticAlias> current,
        IEnumerable<SemanticAlias> overlay)
    {
        var result = current.ToList();
        foreach (var alias in overlay)
        {
            if (!result.Any(existing =>
                    string.Equals(existing.Name, alias.Name, StringComparison.OrdinalIgnoreCase)))
                result.Add(new SemanticAlias(alias.Name, alias.Weight));
        }

        return result.ToArray();
    }

    private static IReadOnlyList<SemanticConstraint> MergeConstraints(
        IEnumerable<SemanticConstraint> current,
        IEnumerable<SemanticConstraint> overlay)
    {
        var result = current.ToList();
        foreach (var constraint in overlay)
        {
            if (!result.Any(existing =>
                    existing.Kind == constraint.Kind &&
                    string.Equals(existing.Value, constraint.Value, StringComparison.Ordinal) &&
                    existing.Minimum == constraint.Minimum &&
                    existing.Maximum == constraint.Maximum))
                result.Add(constraint);
        }

        return result.ToArray();
    }

    /// <summary>
    /// Imports an already generated or independently authored semantic model.
    /// Entity identities must not collide. This lets an application deliberately
    /// mix generated semantics with manually curated semantic entities.
    /// </summary>
    public SemanticModelBuilder Import(SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        foreach (var entity in model.Entities)
        {
            if (_entities.ContainsKey(entity.Id))
                throw new InvalidOperationException($"Semantic entity '{entity.Id}' is already registered.");

            _entities.Add(entity.Id, entity);
        }

        _traversals.AddRange(model.Traversals);
        return this;
    }

    /// <summary>
    /// Defense-in-depth check that a semantic entity's declared fields still
    /// correspond to real properties on its CLR model type. Entities built
    /// through <see cref="Entity{TModel}"/> already satisfy this by
    /// construction (fields are only ever added from a validated property
    /// selector), so this is a safety net against a future refactor
    /// weakening that guarantee rather than something expected to fire today.
    /// It is intentionally only run for <see cref="Entity{TModel}"/>, not for
    /// <see cref="Import"/>: an imported <see cref="SemanticModel"/> may have
    /// been produced by AOT metadata discovery, where a declared field name
    /// legitimately need not equal the CLR property name (e.g. a
    /// <c>[FoundgineField(Name = ...)]</c> override), so the same strict
    /// check would be a false positive there.
    /// </summary>
    private static void ValidateFieldsMatchModelType(SemanticEntity entity)
    {
        if (entity.ModelType is null)
            return;

        var properties = entity.ModelType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var field in entity.Fields)
        {
            if (!properties.TryGetValue(field.Name, out var property))
            {
                throw new InvalidOperationException(
                    $"Semantic entity '{entity.Name}' declares field '{field.Name}', but model type '{entity.ModelType.FullName}' has no matching public property.");
            }

            if (property.PropertyType != field.ClrType)
            {
                throw new InvalidOperationException(
                    $"Semantic entity '{entity.Name}' field '{field.Name}' is typed as '{field.ClrType.Name}', but model type '{entity.ModelType.FullName}' declares '{field.Name}' as '{property.PropertyType.Name}'.");
            }
        }
    }

    /// <summary>
    /// Declares a logical traversal over existing semantic relationships.
    /// Example: Customer -&gt; CustomerRelationship -&gt; Contract -&gt; Transaction
    /// can be exposed as the single open-intent traversal <c>transactions</c>.
    /// Resolution expands it back into the real relationship path before
    /// authorization and planning.
    /// </summary>
    /// <summary>
    /// Declares a logical traversal using semantic names rather than generated
    /// identities. Names are resolved against the already discovered semantic
    /// graph, so application configuration does not need to depend on generated
    /// numeric identifiers.
    /// </summary>
    public SemanticModelBuilder Traversal(
        string sourceEntityName,
        string name,
        params string[] relationshipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEntityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(relationshipPath);
        if (relationshipPath.Length == 0)
            throw new ArgumentException("A semantic traversal must contain at least one relationship.",
                nameof(relationshipPath));

        var source = _entities.Values.FirstOrDefault(x =>
            string.Equals(x.Name, sourceEntityName, StringComparison.OrdinalIgnoreCase));
        if (source is null)
            throw new InvalidOperationException($"Source semantic entity '{sourceEntityName}' is not known.");

        var current = source;
        var ids = new List<RelationshipId>(relationshipPath.Length);
        foreach (var relationshipName in relationshipPath)
        {
            var relationship = current.Relationships.FirstOrDefault(x =>
                string.Equals(x.Name, relationshipName, StringComparison.OrdinalIgnoreCase));
            if (relationship is null)
                throw new InvalidOperationException(
                    $"Traversal '{name}' references relationship '{relationshipName}', which is not declared on '{current.Name}'.");

            ids.Add(relationship.Id);
            current = _entities[relationship.Target];
        }

        return Traversal(source.Id, name, ids.ToArray());
    }

    public SemanticModelBuilder Traversal(
        EntityId source,
        string name,
        params RelationshipId[] path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length == 0)
            throw new ArgumentException("A semantic traversal must contain at least one relationship.", nameof(path));
        if (!_entities.ContainsKey(source))
            throw new InvalidOperationException(
                $"Source semantic entity '{source}' must be registered before declaring traversal '{name}'.");
        if (_entities[source].Relationships.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) ||
            _traversals.Any(x => x.Source == source && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Semantic traversal '{name}' conflicts with an existing relationship or traversal on entity '{source}'.");

        var current = source;
        foreach (var relationshipId in path)
        {
            var entity = _entities[current];
            var relationship = entity.Relationships.FirstOrDefault(x => x.Id == relationshipId)
                               ?? throw new InvalidOperationException(
                                   $"Traversal '{name}' references relationship '{relationshipId}', which is not declared on '{entity.Name}'.");
            current = relationship.Target;
        }

        _traversals.Add(new SemanticTraversal(source, name, current, path.ToArray()));
        return this;
    }

    public SemanticModelBuilder Entity(
        EntityId id,
        string name,
        Action<SemanticEntityBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        if (_entities.ContainsKey(id))
            throw new InvalidOperationException($"Semantic entity '{id}' is already registered.");

        var builder = new SemanticEntityBuilder(id, name);
        configure(builder);
        var entity = builder.Build();
        ValidateEntityAliases(entity);
        _entities.Add(id, entity);
        return this;
    }

    private static PropertyInfo GetProperty<TModel>(Expression<Func<TModel, object?>> expression, Type modelType)
    {
        ArgumentNullException.ThrowIfNull(expression);

        Expression body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
            body = conversion.Operand;

        if (body is not MemberExpression { Member: PropertyInfo property })
        {
            throw new ArgumentException(
                $"The semantic relationship property selector must be a direct property access on {modelType.Name}, such as x => x.Id.",
                nameof(expression));
        }

        if (property.DeclaringType is null || !property.DeclaringType.IsAssignableFrom(modelType))
        {
            throw new ArgumentException(
                $"Property '{property.Name}' does not belong to model type '{modelType.FullName}'.",
                nameof(expression));
        }

        return property;
    }

    public SemanticModel Build()
    {
        // Relationship identities have composition-wide semantics.
        // Validate them before local duplicate checks so independently
        // composed declarations are reconciled by identity first.
        ValidateGlobalRelationshipIdentities();

        foreach (var entity in _entities.Values)
        {
            ValidateUniqueFields(entity);
            ValidateUniqueRelationships(entity);
            ValidateFieldConstraints(entity);

            foreach (var relationship in entity.Relationships)
            {
                if (!_entities.ContainsKey(relationship.Target))
                {
                    throw new InvalidOperationException(
                        $"Semantic relationship '{entity.Name}.{relationship.Name}' targets unknown entity '{relationship.Target}'.");
                }
            }
        }

        foreach (var traversal in _traversals)
        {
            if (traversal.Path.Count == 0)
                throw new InvalidOperationException(
                    $"Semantic traversal '{traversal.Name}' must contain at least one relationship.");
        }

        return new SemanticModel(_entities, _traversals);
    }

    private static void ValidateFieldConstraints(SemanticEntity entity)
    {
        foreach (var field in entity.Fields)
        {
            foreach (var constraint in field.EffectiveConstraints)
            {
                switch (constraint.Kind)
                {
                    case SemanticConstraintKind.Range when constraint.Minimum is null && constraint.Maximum is null:
                        throw new InvalidOperationException(
                            $"Field '{entity.Name}.{field.Name}' declares a Range constraint without a minimum or maximum.");
                    case SemanticConstraintKind.Range when constraint.Minimum is not null &&
                                                           constraint.Maximum is not null &&
                                                           constraint.Minimum > constraint.Maximum:
                        throw new InvalidOperationException(
                            $"Field '{entity.Name}.{field.Name}' declares an invalid Range constraint: minimum exceeds maximum.");
                    case SemanticConstraintKind.Pattern when string.IsNullOrWhiteSpace(constraint.Value):
                        throw new InvalidOperationException(
                            $"Field '{entity.Name}.{field.Name}' declares a Pattern constraint without a pattern.");
                    case SemanticConstraintKind.Currency when string.IsNullOrWhiteSpace(constraint.Value):
                        throw new InvalidOperationException(
                            $"Field '{entity.Name}.{field.Name}' declares a Currency constraint without a currency code.");
                    case SemanticConstraintKind.CountryCode when string.IsNullOrWhiteSpace(constraint.Value):
                        throw new InvalidOperationException(
                            $"Field '{entity.Name}.{field.Name}' declares a CountryCode constraint without a country code.");
                    case SemanticConstraintKind.Temporal when string.IsNullOrWhiteSpace(constraint.Value):
                        throw new InvalidOperationException(
                            $"Field '{entity.Name}.{field.Name}' declares a Temporal constraint without semantics.");
                }
            }
        }
    }

    private static void ValidateUniqueFields(SemanticEntity entity)
    {
        if (entity.Fields.GroupBy(field => field.Id).Any(group => group.Count() > 1))
            throw new InvalidOperationException(
                $"Semantic entity '{entity.Name}' contains duplicate field identities.");

        if (entity.Fields.GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
            throw new InvalidOperationException($"Semantic entity '{entity.Name}' contains duplicate field names.");
    }

    private static void ValidateUniqueRelationships(SemanticEntity entity)
    {
        if (entity.Relationships.GroupBy(relationship => relationship.Id).Any(group => group.Count() > 1))
            throw new InvalidOperationException(
                $"Semantic entity '{entity.Name}' contains duplicate relationship identities.");

        if (entity.Relationships.GroupBy(relationship => relationship.Name, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
            throw new InvalidOperationException(
                $"Semantic entity '{entity.Name}' contains duplicate relationship names.");
    }

    /// <summary>
    /// Relationship identities are globally scoped by their canonical semantic
    /// key, not merely unique within an entity. A 64-bit hash has a theoretical
    /// collision domain, so composition must fail closed if two different
    /// semantic relationships ever resolve to the same identity. This also
    /// protects independently authored modules during Import().
    /// </summary>
    private void ValidateGlobalRelationshipIdentities()
    {
        var seen =
            new Dictionary<RelationshipId, (EntityId Source, string EntityName, string RelationshipName, EntityId Target
                , RelationshipCardinality Cardinality)>();

        foreach (var entity in _entities.Values)
        {
            foreach (var relationship in entity.Relationships)
            {
                if (seen.TryGetValue(relationship.Id, out var existing))
                {
                    var sameCanonicalKey =
                        string.Equals(existing.EntityName, entity.Name, StringComparison.Ordinal) &&
                        string.Equals(existing.RelationshipName, relationship.Name, StringComparison.Ordinal);

                    if (!sameCanonicalKey)
                    {
                        throw new InvalidOperationException(
                            $"Relationship identity collision: '{relationship.Id}' is used by '{existing.EntityName}.{existing.RelationshipName}' and '{entity.Name}.{relationship.Name}'. Relationship identities must be globally unique across composed semantic modules.");
                    }

                    if (existing.Target != relationship.Target || existing.Cardinality != relationship.Cardinality)
                    {
                        throw new InvalidOperationException(
                            $"Relationship identity conflict: '{entity.Name}.{relationship.Name}' resolves to '{relationship.Target}' ({relationship.Cardinality}), but the same canonical relationship was already declared as '{existing.Target}' ({existing.Cardinality}). Composed modules must agree on the target entity and cardinality for an existing relationship identity.");
                    }

                    continue;
                }

                seen.Add(relationship.Id,
                    (entity.Id, entity.Name, relationship.Name, relationship.Target, relationship.Cardinality));
            }
        }
    }

    private void ValidateEntityAliases(SemanticEntity entity)
    {
        var localNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entity.Name };
        foreach (var alias in entity.EffectiveAliases)
        {
            if (!localNames.Add(alias.Name))
                throw new InvalidOperationException(
                    $"Semantic entity alias '{alias.Name}' duplicates the canonical entity name or another alias on '{entity.Name}'.");
            if (_entities.Values.Any(x =>
                    string.Equals(x.Name, alias.Name, StringComparison.OrdinalIgnoreCase) ||
                    x.EffectiveAliases.Any(a => string.Equals(a.Name, alias.Name, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException(
                    $"Semantic entity alias '{alias.Name}' conflicts with an existing semantic entity name or alias.");
        }
    }
}