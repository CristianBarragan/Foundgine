using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Query;

internal static class SemanticFilterValidator
{
    public static void Validate(
        SemanticFilterExpression? filter,
        SemanticEntity entity,
        SemanticContractSnapshot contract)
    {
        if (filter is null) return;
        Visit(filter, entity, contract.Get);
    }

    public static void Validate(
        SemanticFilterExpression? filter,
        SemanticEntity entity,
        SemanticModel model)
    {
        if (filter is null) return;
        Visit(filter, entity, model.Get);
    }

    private static void Visit(
        SemanticFilterExpression expression,
        SemanticEntity entity,
        Func<EntityId, SemanticEntity> getEntity)
    {
        switch (expression)
        {
            case SemanticFieldFilter field:
                if (!IsDeclaredField(entity, field.Field))
                    throw Invalid($"Entity '{entity.Name}' does not declare filter field '{field.Field}'.");
                if (!IsFieldFilterable(entity, field.Field))
                    throw Invalid($"Field '{entity.Name}.{field.Field}' is not filterable.");
                if (field.Operator == SemanticFilterOperator.In && field.Value is null)
                    throw Invalid($"IN filter on '{entity.Name}.{field.Field}' requires a value list.");
                if (field.Value is not null && field.Field != entity.Identity.FieldId)
                    SemanticValueValidator.Validate(field.Value, GetField(entity, field.Field),
                        field.Operator.ToString());
                break;

            case SemanticRelationshipFilter relationshipFilter:
                var relationship = entity.Relationships.FirstOrDefault(x => x.Id == relationshipFilter.Relationship);
                if (relationship is null)
                    throw Invalid(
                        $"Entity '{entity.Name}' does not declare filter relationship '{relationshipFilter.Relationship}'.");

                var target = getEntity(relationship.Target);
                Visit(relationshipFilter.Predicate, target, getEntity);
                break;

            case SemanticAggregateFilter aggregate:
                var aggregateRelationship = entity.Relationships.FirstOrDefault(x => x.Id == aggregate.Relationship);
                if (aggregateRelationship is null)
                    throw Invalid(
                        $"Entity '{entity.Name}' does not declare aggregate filter relationship '{aggregate.Relationship}'.");
                if (aggregateRelationship.Cardinality != RelationshipCardinality.Many)
                    throw Invalid("Aggregate filters are only valid on collection relationships.");

                var aggregateTarget = getEntity(aggregateRelationship.Target);
                if (aggregate.Predicate is not null)
                    Visit(aggregate.Predicate, aggregateTarget, getEntity);
                if (aggregate.Aggregate == SemanticFilterAggregate.Count)
                {
                    if (aggregate.Field is not null)
                        throw Invalid("COUNT aggregate filters do not accept a target field.");
                }
                else
                {
                    if (aggregate.Field is null)
                        throw Invalid($"{aggregate.Aggregate} aggregate filters require a target field.");
                    if (!IsDeclaredField(aggregateTarget, aggregate.Field.Value))
                        throw Invalid(
                            $"Aggregate filter field '{aggregate.Field}' is not defined on '{aggregateTarget.Name}'.");
                    if (!IsFieldAggregatable(aggregateTarget, aggregate.Field.Value))
                        throw Invalid($"Field '{aggregateTarget.Name}.{aggregate.Field}' is not aggregatable.");
                    if (aggregate.Value is not null && aggregate.Field.Value != aggregateTarget.Identity.FieldId)
                        SemanticValueValidator.Validate(aggregate.Value,
                            GetField(aggregateTarget, aggregate.Field.Value), aggregate.Operator.ToString());
                }

                break;

            case SemanticAndFilter andFilter:
                if (andFilter.Expressions.Count == 0)
                    throw Invalid("AND filter cannot be empty.");
                foreach (var child in andFilter.Expressions)
                    Visit(child, entity, getEntity);
                break;

            case SemanticOrFilter orFilter:
                if (orFilter.Expressions.Count == 0)
                    throw Invalid("OR filter cannot be empty.");
                foreach (var child in orFilter.Expressions)
                    Visit(child, entity, getEntity);
                break;

            default:
                throw Invalid($"Unsupported semantic filter '{expression.GetType().Name}'.");
        }
    }

    private static bool IsFieldFilterable(SemanticEntity entity, FieldId fieldId) =>
        entity.Identity.FieldId == fieldId ||
        entity.Fields.FirstOrDefault(x => x.Id == fieldId)?.Capabilities
            .HasFlag(SemanticFieldCapabilities.Filterable) == true;

    private static bool IsFieldAggregatable(SemanticEntity entity, FieldId fieldId) =>
        entity.Identity.FieldId == fieldId ||
        entity.Fields.FirstOrDefault(x => x.Id == fieldId)?.Capabilities
            .HasFlag(SemanticFieldCapabilities.Aggregatable) == true;

    private static SemanticField GetField(SemanticEntity entity, FieldId fieldId) =>
        entity.Fields.FirstOrDefault(x => x.Id == fieldId) ??
        new SemanticField(entity.Identity.FieldId, entity.Identity.Name, typeof(object));

    private static bool IsDeclaredField(SemanticEntity entity, FieldId fieldId) =>
        entity.Identity.FieldId == fieldId || entity.Fields.Any(x => x.Id == fieldId);

    private static InvalidOperationException Invalid(string message) =>
        new($"Invalid semantic filter: {message}");
}