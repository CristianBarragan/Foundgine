using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Mutation;

namespace Foundgine.Core.Semantic.Planning.Mutation;

/// <summary>
/// Lowers a semantic mutation plan into provider-neutral execution work.
/// This is the first boundary where semantic FieldId values are resolved to
/// physical ColumnId values. Provider-specific SQL remains outside this type.
/// </summary>
public sealed class SemanticMutationExecutionLowerer
{
    private readonly IMutationSchema _schema;

    public SemanticMutationExecutionLowerer(IMutationSchema schema) =>
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));

    public ExecutionMutationIR Lower(SemanticMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Operations.Count == 0)
            throw new InvalidOperationException(
                "A semantic mutation plan must contain at least one operation.");

        var operations = new List<MutationOperation>(plan.Operations.Count);

        foreach (var operation in plan.Operations)
        {
            var entity = _schema.GetEntity(operation.Entity);
            var fields = new List<MutationFieldValue>(operation.Fields.Count);

            foreach (var field in operation.Fields)
            {
                if (!entity.Fields.TryGetValue(field.Field, out var column) || column is null)
                    throw new InvalidOperationException(
                        $"Semantic mutation field '{field.Field.Value}' is not writable on '{entity.Name}'.");

                MutationValueReference? source = field.Source is { } reference
                    ? new MutationValueReference(
                        reference.SourceOperationIndex,
                        reference.SourceField)
                    : null;

                fields.Add(new MutationFieldValue(column.Value, field.Value, source));
            }

            IReadOnlyList<ColumnId>? conflicts = null;
            if (operation.Kind == SemanticMutationKind.Upsert)
            {
                var mapped = operation.ConflictFields
                    .Select(field => entity.Fields.TryGetValue(field, out var column) && column is not null
                        ? column.Value
                        : throw new InvalidOperationException(
                            $"Semantic conflict field '{field.Value}' is not mapped on '{entity.Name}'."))
                    .ToArray();

                if (mapped.Length == 0)
                    throw new InvalidOperationException(
                        $"Upsert for '{entity.Name}' requires semantic conflict fields.");

                conflicts = mapped;
            }

            Validate(operation, entity);

            operations.Add(new MutationOperation(
                entity,
                operation.Kind switch
                {
                    SemanticMutationKind.Create => MutationKind.Create,
                    SemanticMutationKind.Update => MutationKind.Update,
                    SemanticMutationKind.Delete => MutationKind.Delete,
                    SemanticMutationKind.Upsert => MutationKind.Upsert,
                    _ => throw new ArgumentOutOfRangeException()
                },
                fields,
                operation.Filter,
                conflicts,
                operation.ReturnFields));
        }

        // Semantic dependencies are the single source of truth. At this boundary
        // semantic FieldIds are resolved to physical target ColumnIds. The
        // provider-specific correlation carrier is introduced later by the SQL
        // compiler, not represented as a second semantic edge collection.
        var dependencies = plan.Dependencies.Select(d =>
        {
            if (!int.TryParse(d.FromOperationId, out var source) ||
                !int.TryParse(d.ToOperationId, out var target))
                throw new InvalidOperationException(
                    "Semantic mutation operation IDs must be stable numeric ordinals for execution lowering.");

            if (source < 0 || source >= operations.Count ||
                target < 0 || target >= operations.Count ||
                source >= target)
                throw new InvalidOperationException(
                    $"Invalid semantic mutation dependency {source} -> {target}.");

            var targetEntity = operations[target].Entity;
            if (!targetEntity.Fields.TryGetValue(d.TargetField, out var targetColumn) ||
                targetColumn is null)
                throw new InvalidOperationException(
                    $"Semantic dependency target field '{d.TargetField.Value}' is not writable on '{targetEntity.Name}'.");

            return new MutationDependency(source, target, d.SourceField, targetColumn.Value);
        }).ToArray();

        return ExecutionMutationIR.From(
            new MutationBatchPlan(operations, dependencies),
            plan.RequiredSecurityInvariants);
    }

    private static void Validate(
        SemanticMutationOperationPlan operation,
        MutationEntitySchema entity)
    {
        if (operation.Kind is SemanticMutationKind.Update or SemanticMutationKind.Delete &&
            operation.Filter is null)
            throw new InvalidOperationException(
                $"Unfiltered {operation.Kind} mutations are not permitted for '{entity.Name}'.");

        if (operation.Kind == SemanticMutationKind.Delete && operation.Fields.Count != 0)
            throw new InvalidOperationException("Delete mutations cannot contain field values.");

        if (operation.Kind != SemanticMutationKind.Delete && operation.Fields.Count == 0)
            throw new InvalidOperationException(
                $"{operation.Kind} mutations must contain at least one field value.");

        foreach (var field in operation.ReturnFields)
            if (!entity.Fields.ContainsKey(field))
                throw new InvalidOperationException(
                    $"Return field '{field.Value}' is not registered on '{entity.Name}'.");
    }
}