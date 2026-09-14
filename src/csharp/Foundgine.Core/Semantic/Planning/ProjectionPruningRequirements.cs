using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Derives fields that must remain available to evaluate a semantic node.
/// Output fields are always retained; query filters and ordering add their
/// root-level field dependencies. Relationship and aggregate dependencies are
/// represented as relationship-level requirements and are therefore not
/// removed by the conservative projection rule.
/// </summary>
public static class ProjectionPruningRequirements
{
    public static IReadOnlySet<FieldId> RequiredRootFields(SemanticPlanNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var required = new HashSet<FieldId>(node.Fields);
        var options = node.QueryOptions;
        if (options is null)
            return required;

        CollectFilterFields(options.Filter, required);
        foreach (var order in options.EffectiveOrder)
        {
            if (order.IsRootField && !order.IsAggregate)
                required.Add(order.Field);
        }

        return required;
    }

    private static void CollectFilterFields(SemanticFilterExpression? filter, ISet<FieldId> required)
    {
        switch (filter)
        {
            case SemanticFieldFilter field:
                required.Add(field.Field);
                return;
            case SemanticRelationshipFilter relationship:
                // The relationship boundary itself is required, but its target
                // fields belong to the child/relationship execution scope.
                CollectFilterFields(relationship.Predicate, required);
                return;
            case SemanticAggregateFilter aggregate:
                if (aggregate.Field is not null)
                    required.Add(aggregate.Field.Value);
                if (aggregate.Predicate is not null)
                    CollectFilterFields(aggregate.Predicate, required);
                return;
            case SemanticAndFilter and:
                foreach (var expression in and.Expressions)
                    CollectFilterFields(expression, required);
                return;
            case SemanticOrFilter or:
                foreach (var expression in or.Expressions)
                    CollectFilterFields(expression, required);
                return;
        }
    }
}
