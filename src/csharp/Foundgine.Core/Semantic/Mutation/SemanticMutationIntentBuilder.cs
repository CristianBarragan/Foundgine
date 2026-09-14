using System;
using System.Collections.Generic;
using System.Linq;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic;

namespace Foundgine.Core.Semantic.Mutation;

/// <summary>
/// Developer-friendly open mutation authoring surface. It resolves entity and
/// field names against the semantic model but emits the same canonical
/// SemanticMutationOperationGraph used by GraphQL, MCP and direct callers.
/// Operation aliases make generated-value dependencies readable without
/// exposing provider or column concepts.
/// </summary>
public sealed class SemanticMutationIntentBuilder
{
    private readonly SemanticModel _model;
    private readonly List<SemanticMutationOperation> _operations = [];
    private readonly Dictionary<string, int> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private int _current = -1;

    public SemanticMutationIntentBuilder(SemanticModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public SemanticMutationOperationBuilder Create(string entity, string? alias = null) =>
        Begin(entity, SemanticMutationKind.Create, alias);

    public SemanticMutationOperationBuilder Update(string entity, string? alias = null) =>
        Begin(entity, SemanticMutationKind.Update, alias);

    public SemanticMutationOperationBuilder Delete(string entity, string? alias = null) =>
        Begin(entity, SemanticMutationKind.Delete, alias);

    public SemanticMutationOperationBuilder Upsert(string entity, string? alias = null) =>
        Begin(entity, SemanticMutationKind.Upsert, alias);

    public SemanticMutationOperationGraph Build()
    {
        if (_operations.Count == 0)
            throw new InvalidOperationException(
                "A semantic mutation intent must contain at least one operation.");

        foreach (var operation in _operations)
        {
            if ((operation.Kind is SemanticMutationKind.Update or SemanticMutationKind.Delete) &&
                operation.Filter is null)
            {
                throw new InvalidOperationException(
                    $"{operation.Kind} mutations require a target filter.");
            }

            if (operation.Kind == SemanticMutationKind.Upsert &&
                operation.ConflictFields.Count == 0)
            {
                throw new InvalidOperationException(
                    "Upsert mutations require conflict fields.");
            }
        }

        return new SemanticMutationOperationGraph(_operations.ToArray());
    }

    private SemanticMutationOperationBuilder Begin(
        string entityName,
        SemanticMutationKind kind,
        string? alias)
    {
        var entity = FindEntity(entityName);

        if (!string.IsNullOrWhiteSpace(alias) &&
            !_aliases.TryAdd(alias, _operations.Count))
        {
            throw new InvalidOperationException(
                $"Mutation operation alias '{alias}' is already registered.");
        }

        var operation = kind switch
        {
            SemanticMutationKind.Create =>
                SemanticMutationBuilder.Create(entity.Id, []),

            SemanticMutationKind.Update =>
                SemanticMutationBuilder.Update(entity.Id, []),

            SemanticMutationKind.Delete =>
                new SemanticMutationOperation(
                    entity.Id,
                    SemanticMutationKind.Delete,
                    [],
                    null,
                    [],
                    [entity.Identity.FieldId],
                    [
                        new SemanticMutationEffect(
                            SemanticMutationEffectKind.DeleteEntity,
                            entity.Id)
                    ],
                    []),

            SemanticMutationKind.Upsert =>
                SemanticMutationBuilder.Upsert(
                    entity.Id,
                    [],
                    [],
                    []),

            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        _operations.Add(operation);
        _current = _operations.Count - 1;

        return new SemanticMutationOperationBuilder(this, _current);
    }

    internal SemanticMutationIntentBuilder Set(
        int index,
        string fieldName,
        object? value)
    {
        var operation = Get(index);
        var field = FindField(operation.Entity, fieldName);

        var fields = operation.Fields.ToList();
        fields.Add(new SemanticMutationField(field.Id, value));

        Replace(index, operation with
        {
            Fields = fields,
            Effects = BuildEffects(operation, fields)
        });

        return this;
    }

    internal SemanticMutationIntentBuilder SetFrom(
        int index,
        string fieldName,
        string sourceAlias,
        string sourceFieldName)
    {
        if (!_aliases.TryGetValue(sourceAlias, out var sourceIndex))
        {
            throw new InvalidOperationException(
                $"Unknown mutation operation alias '{sourceAlias}'.");
        }

        if (sourceIndex >= index)
        {
            throw new InvalidOperationException(
                "Mutation value dependencies must reference an earlier operation.");
        }

        var operation = Get(index);
        var targetField = FindField(operation.Entity, fieldName);
        var sourceOperation = Get(sourceIndex);
        var sourceField = FindField(sourceOperation.Entity, sourceFieldName);

        var fields = operation.Fields.ToList();

        fields.Add(new SemanticMutationField(
            targetField.Id,
            null,
            new SemanticMutationValueReference(
                sourceIndex,
                sourceField.Id)));

        Replace(index, operation with
        {
            Fields = fields,
            Effects = BuildEffects(operation, fields)
        });

        return this;
    }

    internal SemanticMutationIntentBuilder Return(
        int index,
        params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var operation = Get(index);

        IReadOnlyList<FieldId> result = fields.Length == 0
            ? [_model.Get(operation.Entity).Identity.FieldId]
            : fields
                .Select(name => FindField(operation.Entity, name).Id)
                .Distinct()
                .ToArray();

        Replace(index, operation with
        {
            ReturnFields = result
        });

        return this;
    }

    internal SemanticMutationIntentBuilder Conflict(
        int index,
        params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var operation = Get(index);

        IReadOnlyList<FieldId> conflicts = fields.Length == 0
            ? [_model.Get(operation.Entity).Identity.FieldId]
            : fields
                .Select(name => FindField(operation.Entity, name).Id)
                .Distinct()
                .ToArray();

        Replace(index, operation with
        {
            ConflictFields = conflicts
        });

        return this;
    }

    internal SemanticMutationIntentBuilder Where(
        int index,
        string fieldName,
        SemanticFilterOperator op,
        object? value)
    {
        var operation = Get(index);

        if (operation.Kind is SemanticMutationKind.Create or SemanticMutationKind.Upsert)
        {
            throw new InvalidOperationException(
                $"Mutation kind '{operation.Kind}' does not accept a target filter.");
        }

        var field = FindField(operation.Entity, fieldName);

        Replace(index, operation with
        {
            Filter = new SemanticFieldFilter(field.Id, op, value)
        });

        return this;
    }

    internal SemanticMutationIntentBuilder Connect(
        int index,
        string relationshipName)
    {
        var operation = Get(index);
        var relationship = FindRelationship(
            operation.Entity,
            relationshipName);

        var effects = operation.Effects.ToList();

        effects.Add(
            new SemanticMutationEffect(
                SemanticMutationEffectKind.ConnectRelationship,
                operation.Entity,
                null,
                relationship.Id));

        Replace(index, operation with
        {
            Effects = effects
        });

        return this;
    }

    private SemanticMutationOperation Get(int index) =>
        index >= 0 && index < _operations.Count
            ? _operations[index]
            : throw new ArgumentOutOfRangeException(nameof(index));

    private void Replace(
        int index,
        SemanticMutationOperation operation) =>
        _operations[index] = operation;

    private SemanticEntity FindEntity(string name) =>
        _model.Entities.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) ||
                                            x.EffectiveAliases.Any(a =>
                                                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
        ?? throw new InvalidOperationException(
            $"Unknown semantic mutation entity '{name}'.");

    private SemanticField FindField(
        EntityId entityId,
        string name)
    {
        var entity = _model.Get(entityId);

        return entity.Fields.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) ||
                                                 x.EffectiveAliases.Any(a =>
                                                     string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
               ?? (string.Equals(
                   entity.Identity.Name,
                   name,
                   StringComparison.OrdinalIgnoreCase)
                   ? new SemanticField(
                       entity.Identity.FieldId,
                       entity.Identity.Name,
                       typeof(object),
                       Capabilities:
                       SemanticFieldCapabilities.Default |
                       SemanticFieldCapabilities.Writable)
                   : throw new InvalidOperationException(
                       $"Unknown semantic mutation field '{entity.Name}.{name}'."));
    }

    private SemanticRelationship FindRelationship(
        EntityId entityId,
        string name)
    {
        var entity = _model.Get(entityId);

        return entity.Relationships.FirstOrDefault(x =>
                   string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) ||
                   x.EffectiveAliases.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
               ?? throw new InvalidOperationException(
                   $"Unknown semantic mutation relationship '{entity.Name}.{name}'.");
    }

    private static IReadOnlyList<SemanticMutationEffect> BuildEffects(
        SemanticMutationOperation original,
        IReadOnlyList<SemanticMutationField> fields)
    {
        var effects = original.Effects
            .Where(x => x.Kind is not SemanticMutationEffectKind.SetField)
            .ToList();

        effects.AddRange(
            fields.Select(x => new SemanticMutationEffect(
                SemanticMutationEffectKind.SetField,
                original.Entity,
                x.Field)));

        return effects;
    }
}

public sealed class SemanticMutationOperationBuilder
{
    private readonly SemanticMutationIntentBuilder _owner;
    private readonly int _index;

    internal SemanticMutationOperationBuilder(
        SemanticMutationIntentBuilder owner,
        int index)
    {
        _owner = owner;
        _index = index;
    }

    public SemanticMutationOperationBuilder Set(
        string field,
        object? value)
    {
        _owner.Set(_index, field, value);
        return this;
    }

    public SemanticMutationOperationBuilder SetFrom(
        string field,
        string sourceAlias,
        string sourceField)
    {
        _owner.SetFrom(
            _index,
            field,
            sourceAlias,
            sourceField);

        return this;
    }

    public SemanticMutationOperationBuilder Return(
        params string[] fields)
    {
        _owner.Return(_index, fields);
        return this;
    }

    public SemanticMutationOperationBuilder Conflict(
        params string[] fields)
    {
        _owner.Conflict(_index, fields);
        return this;
    }

    public SemanticMutationOperationBuilder Where(
        string field,
        SemanticFilterOperator op,
        object? value)
    {
        _owner.Where(
            _index,
            field,
            op,
            value);

        return this;
    }

    public SemanticMutationOperationBuilder Connect(
        string relationship)
    {
        _owner.Connect(
            _index,
            relationship);

        return this;
    }

    // These forwarding methods keep advanced mutation batches fluent while
    // preserving the operation currently being configured.
    public SemanticMutationOperationBuilder Create(
        string entity,
        string? alias = null) =>
        _owner.Create(entity, alias);

    public SemanticMutationOperationBuilder Update(
        string entity,
        string? alias = null) =>
        _owner.Update(entity, alias);

    public SemanticMutationOperationBuilder Delete(
        string entity,
        string? alias = null) =>
        _owner.Delete(entity, alias);

    public SemanticMutationOperationBuilder Upsert(
        string entity,
        string? alias = null) =>
        _owner.Upsert(entity, alias);

    // Allow a fluent mutation operation chain to terminate directly in
    // the canonical semantic mutation graph.
    public SemanticMutationOperationGraph Build() =>
        _owner.Build();

    public SemanticMutationIntentBuilder Next =>
        _owner;
}