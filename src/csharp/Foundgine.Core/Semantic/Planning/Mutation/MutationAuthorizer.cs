using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Planning.Mutation;

/// <summary>
/// Applies semantic write authorization to a provider-independent mutation
/// plan. It deliberately sits outside MutationPlanner so planning remains a
/// structural concern while policy remains a semantic concern.
/// </summary>
public sealed class MutationAuthorizer
{
    private readonly IMutationSchema _schema;
    private readonly ISemanticAuthorizationPolicy _policy;

    public MutationAuthorizer(IMutationSchema schema, ISemanticAuthorizationPolicy policy)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public MutationPlan Authorize(MutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        foreach (var operation in plan.Operations)
            Authorize(operation);
        return plan;
    }


    /// <summary>
    /// Authorizes the canonical semantic mutation plan directly. The authorized
    /// semantic representation is therefore the same representation that is
    /// subsequently lowered for execution; no independently reconstructed batch
    /// can become the execution source of truth.
    /// </summary>
    public SemanticMutationPlan Authorize(SemanticMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var operation in plan.Operations)
        {
            var entity = _schema.GetEntity(operation.Entity);
            RequireAllowed(
                _policy.GetEntityAccess(entity.Id, AuthorizationOperation.Write),
                $"write entity '{entity.Name}'");

            foreach (var field in operation.Fields)
            {
                if (!entity.Fields.ContainsKey(field.Field))
                    throw new InvalidOperationException(
                        $"Mutation field '{field.Field.Value}' is not registered on '{entity.Name}'.");

                RequireAllowed(
                    _policy.GetFieldAccess(entity.Id, field.Field, AuthorizationOperation.Write),
                    $"write field '{entity.Name}.{field.Field.Value}'");
            }

            foreach (var field in operation.ConflictFields)
            {
                if (!entity.Fields.ContainsKey(field))
                    throw new InvalidOperationException(
                        $"Conflict field '{field.Value}' is not registered on '{entity.Name}'.");

                RequireAllowed(
                    _policy.GetFieldAccess(entity.Id, field, AuthorizationOperation.Write),
                    $"write conflict field '{entity.Name}.{field.Value}'");
            }

            foreach (var field in operation.ReturnFields)
            {
                if (!entity.Fields.ContainsKey(field))
                    throw new InvalidOperationException(
                        $"Return field '{field.Value}' is not registered on '{entity.Name}'.");

                RequireAllowed(
                    _policy.GetFieldAccess(entity.Id, field, AuthorizationOperation.Read),
                    $"read return field '{entity.Name}.{field.Value}'");
            }

            ValidateFilter(operation.Filter, entity);
        }

        return plan;
    }

    public MutationBatchPlan Authorize(MutationBatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        foreach (var operation in plan.Operations)
            Authorize(operation);
        return plan;
    }

    private void Authorize(MutationOperation operation)
    {
        var entity = operation.Entity;
        RequireAllowed(
            _policy.GetEntityAccess(entity.Id, AuthorizationOperation.Write),
            $"write entity '{entity.Name}'");

        foreach (var field in operation.Fields)
        {
            var fieldMapping = entity.Fields.FirstOrDefault(pair => pair.Value == field.Column);
            if (fieldMapping.Equals(default(KeyValuePair<FieldId, ColumnId?>)))
                throw new InvalidOperationException(
                    $"Mutation field column '{field.Column.Value}' has no semantic field mapping on '{entity.Name}'.");

            var fieldId = fieldMapping.Key;

            RequireAllowed(
                _policy.GetFieldAccess(entity.Id, fieldId, AuthorizationOperation.Write),
                $"write field '{entity.Name}.{fieldId.Value}'");
        }

        foreach (var fieldId in operation.ReturnFields ?? Array.Empty<FieldId>())
        {
            RequireAllowed(
                _policy.GetFieldAccess(entity.Id, fieldId, AuthorizationOperation.Read),
                $"read return field '{entity.Name}.{fieldId.Value}'");
        }

        ValidateFilter(operation.Filter, entity);
    }

    private void ValidateFilter(SemanticFilterExpression? filter, MutationEntitySchema entity)
    {
        if (filter is null)
            return;

        switch (filter)
        {
            case SemanticFieldFilter field:
                RequireAllowed(
                    _policy.GetFieldAccess(entity.Id, field.Field, AuthorizationOperation.Read),
                    $"filter on field '{entity.Name}.{field.Field.Value}'");
                break;

            case SemanticRelationshipFilter relationship:
                RequireAllowed(
                    _policy.GetRelationshipAccess(entity.Id, relationship.Relationship, AuthorizationOperation.Read),
                    $"filter through relationship '{relationship.Relationship.Value}'");
                var relationshipSchema = _schema.GetRelationship(relationship.Relationship);
                ValidateFilter(relationship.Predicate, _schema.GetEntity(relationshipSchema.Target));
                break;

            case SemanticAggregateFilter aggregate:
                RequireAllowed(
                    _policy.GetRelationshipAccess(entity.Id, aggregate.Relationship, AuthorizationOperation.Read),
                    $"aggregate filter through relationship '{aggregate.Relationship.Value}'");
                if (aggregate.Field is { } aggregateField)
                {
                    var aggregateRelationshipSchema = _schema.GetRelationship(aggregate.Relationship);
                    RequireAllowed(
                        _policy.GetFieldAccess(aggregateRelationshipSchema.Target, aggregateField,
                            AuthorizationOperation.Read),
                        $"aggregate filter field '{aggregateField.Value}'");
                }

                break;

            case SemanticAndFilter and:
                foreach (var expression in and.Expressions)
                    ValidateFilter(expression, entity);
                break;

            case SemanticOrFilter or:
                foreach (var expression in or.Expressions)
                    ValidateFilter(expression, entity);
                break;

            default:
                throw new NotSupportedException(
                    $"Mutation authorization does not support filter '{filter.GetType().Name}'.");
        }
    }

    private static void RequireAllowed(AuthorizationDecision decision, string resource)
    {
        if (!decision.IsAllowed)
            throw new SemanticAuthorizationException($"Access denied for {resource}.");
    }
}