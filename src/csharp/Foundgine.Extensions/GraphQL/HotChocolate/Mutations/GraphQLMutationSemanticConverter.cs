using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Extensions.GraphQL.HotChocolate;

/// <summary>
/// Converts the GraphQL adapter's legacy nested intent representation into the
/// canonical semantic mutation graph. Security-sensitive GraphQL execution must
/// use this boundary so the mutation engine can apply warrant validation,
/// policy authorization and the final execution security gate to the exact
/// operation graph that came from GraphQL.
/// </summary>
public static class GraphQLMutationSemanticConverter
{
    public static SemanticMutationOperationGraph ToSemanticGraph(
        IReadOnlyList<NestedMutationIntent> intents,
        IMutationSchema schema)
    {
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(schema);
        if (intents.Count == 0)
            throw new InvalidOperationException("A GraphQL mutation batch must contain at least one mutation intent.");

        var operations = new List<SemanticMutationOperation>();

        foreach (var intent in intents)
            Visit(intent, parentIndex: null, relationship: null);

        return new SemanticMutationOperationGraph(operations);

        void Visit(
            NestedMutationIntent intent,
            int? parentIndex,
            MutationRelationshipSchema? relationship)
        {
            var index = operations.Count;
            var operation = ConvertOperation(intent.Mutation, schema);
            operations.Add(operation);

            if (parentIndex is { } parent)
            {
                if (relationship is null)
                    throw new InvalidOperationException("Nested GraphQL mutation relationship metadata is missing.");

                if (relationship.Source != operations[parent].Entity ||
                    relationship.Target != operation.Entity)
                {
                    throw new InvalidOperationException(
                        $"GraphQL nested mutation relationship '{relationship.Name}' does not connect " +
                        $"'{operations[parent].Entity.Value}' to '{operation.Entity.Value}'.");
                }

                var parentSchema = schema.GetEntity(operations[parent].Entity);
                var primaryKeyColumn = parentSchema.PrimaryKeyColumn
                                       ?? throw new InvalidOperationException(
                                           $"Parent entity '{parentSchema.Name}' requires a primary key for nested mutation propagation.");

                if (primaryKeyColumn != relationship.SourceColumn)
                    throw new InvalidOperationException(
                        $"Nested mutation relationship '{relationship.Name}' must originate at the parent primary key.");

                var parentPkField = parentSchema.Fields.FirstOrDefault(x => x.Value == primaryKeyColumn).Key;
                if (parentPkField == default)
                    throw new InvalidOperationException(
                        $"Parent primary key column '{primaryKeyColumn.Value}' has no semantic field mapping.");

                var childSchema = schema.GetEntity(operation.Entity);
                var childField = childSchema.Fields.FirstOrDefault(x => x.Value == relationship.TargetColumn).Key;
                if (childField == default)
                    throw new InvalidOperationException(
                        $"Nested relationship target column '{relationship.TargetColumn.Value}' has no semantic field mapping.");

                if (!operations[parent].ReturnFields.Contains(parentPkField))
                {
                    throw new InvalidOperationException(
                        $"Parent mutation '{parentSchema.Name}' must return its primary key field '{parentPkField.Value}' for nested propagation.");
                }

                var fields = operation.Fields.ToList();
                if (fields.Any(x => x.Field == childField && x.Source is null))
                    throw new InvalidOperationException(
                        $"Nested mutation '{childSchema.Name}' explicitly supplies relationship field '{childField.Value}'.");

                if (!fields.Any(x => x.Field == childField && x.Source is not null))
                {
                    fields.Add(new SemanticMutationField(
                        childField,
                        null,
                        new SemanticMutationValueReference(parent, parentPkField)));
                }

                operation = operation with
                {
                    Fields = fields,
                    Dependencies =
                    [
                        .. operation.Dependencies,
                        new SemanticMutationDependency(
                            parent,
                            index,
                            parentPkField,
                            childField,
                            relationship.Id)
                    ],
                    Effects = operation.Effects
                };
                operations[index] = operation;
            }

            foreach (var child in intent.Children)
            {
                var relation = schema.GetRelationship(child.Relationship);
                Visit(child.Mutation, index, relation);
            }
        }
    }

    private static SemanticMutationOperation ConvertOperation(
        IMutationIntent intent,
        IMutationSchema schema)
    {
        var entity = schema.GetEntity(intent.Entity);
        var fields = intent switch
        {
            MutationIntent mutation => mutation.Fields.Select(x => ToSemanticField(entity, x)).ToArray(),
            UpsertIntent upsert => upsert.Fields.Select(x => ToSemanticField(entity, x)).ToArray(),
            _ => throw new NotSupportedException(
                $"GraphQL mutation intent '{intent.GetType().Name}' is not supported.")
        };

        return intent switch
        {
            MutationIntent mutation => mutation.Kind switch
            {
                MutationKind.Create => SemanticMutationBuilder.Create(
                    entity.Id, fields, mutation.ReturnFields),
                MutationKind.Update => SemanticMutationBuilder.Update(
                    entity.Id, fields, mutation.Filter, mutation.ReturnFields),
                MutationKind.Delete => SemanticMutationBuilder.Delete(
                    entity.Id,
                    mutation.Filter ?? throw new InvalidOperationException("Delete mutation requires a filter."),
                    mutation.ReturnFields),
                _ => throw new NotSupportedException($"Mutation kind '{mutation.Kind}' is not supported.")
            },
            UpsertIntent upsert => SemanticMutationBuilder.Upsert(
                entity.Id,
                fields,
                upsert.ConflictColumns?.Select(ToFieldId).ToArray()
                ?? Array.Empty<FieldId>(),
                upsert.ReturnFields),
            _ => throw new NotSupportedException()
        };

        FieldId ToFieldId(ColumnId column) =>
            entity.Fields.FirstOrDefault(x => x.Value == column).Key;
    }

    private static SemanticMutationField ToSemanticField(
        MutationEntitySchema entity,
        MutationFieldValue field)
    {
        var semanticField = entity.Fields.FirstOrDefault(x => x.Value == field.Column).Key;
        if (semanticField == default)
            throw new InvalidOperationException(
                $"GraphQL mutation column '{field.Column.Value}' is not mapped to a semantic field on '{entity.Name}'.");

        var source = field.Source is { } reference
            ? new SemanticMutationValueReference(reference.SourceOperationIndex, reference.SourceField)
            : null;

        return new SemanticMutationField(semanticField, field.Value, source);
    }
}