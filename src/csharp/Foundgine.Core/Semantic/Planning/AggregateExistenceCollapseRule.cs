using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Aggregates;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Collapses a bare COUNT comparison with an embedded relationship predicate into the
/// equivalent relationship quantifier:
/// COUNT(R WHERE P) > 0 / greater or equal 1 / different than 0 -> SOME(R, P)
/// COUNT(R WHERE P) = 0 / smaller than 1 / smaller than 0 -> NONE(R, P)
///
/// The rule is provider-aware by design: relationship-quantifier support must be explicitly
/// declared by the target provider before the rewrite is allowed to fire.
/// </summary>
public sealed class AggregateExistenceCollapseRule : IPlanRewriteRule
{
    private readonly AggregateProviderCapability _providerCapability;

    public AggregateExistenceCollapseRule(AggregateProviderCapability providerCapability)
    {
        _providerCapability = providerCapability ?? throw new ArgumentNullException(nameof(providerCapability));
    }

    public string Name => "aggregate.existence.collapse";

    public IReadOnlyList<string> Preconditions =>
    [
        "COUNT aggregate has no target field",
        "COUNT aggregate has a relationship predicate",
        "COUNT comparison is provably an existence/emptiness test",
        "provider supports relationship quantifiers"
    ];

    public IReadOnlyList<string> SecurityObligations =>
    [
        "authorization.required",
        "authorization.runtime",
        "visibility.relationship",
        "planning.cache-context-isolation"
    ];

    public double CostImpact => 0.75d;
    public double BenefitEstimate => 5.0d;
    public bool IsIdempotent => true;
    public int Priority => 40;

    public IReadOnlyList<string> MustRunBefore => [];

    public bool CanApply(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return _providerCapability.SupportsRelationshipQuantifiers && ContainsEligible(plan.Root);
    }

    public SemanticPlan Apply(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!CanApply(plan))
            return plan;

        var candidate = RewritePlan(plan);
        if (ReferenceEquals(candidate, plan))
            return plan;

        // The proof is deliberately constructed after the rewrite. Any mismatch between the
        // canonical semantic identity, predicate shape, provider capability, or security
        // contract fails closed and prevents the rewritten plan from escaping this rule.
        _ = AggregateExistenceCollapseProof.Create(
            plan,
            candidate,
            _providerCapability,
            PredicateShapeMatches(plan, candidate));

        return candidate;
    }

    private static SemanticPlan RewritePlan(SemanticPlan plan)
    {
        var changed = false;
        var root = RewriteNode(plan.Root, ref changed);
        return changed ? new SemanticPlan(root, plan.RequiredSecurityInvariants, plan.AuthorizationBinding) : plan;
    }

    private static SemanticPlanNode RewriteNode(SemanticPlanNode node, ref bool changed)
    {
        var options = node.QueryOptions;
        if (options?.Filter is not null)
        {
            var rewritten = RewriteFilter(options.Filter, ref changed);
            if (!ReferenceEquals(rewritten, options.Filter))
                options = options with { Filter = rewritten };
        }

        var children = new SemanticPlanNode[node.Children.Count];
        var childrenChanged = false;
        for (var i = 0; i < node.Children.Count; i++)
        {
            children[i] = RewriteNode(node.Children[i], ref changed);
            childrenChanged |= !ReferenceEquals(children[i], node.Children[i]);
        }

        if (childrenChanged)
            changed = true;

        return changed && (options is not null || childrenChanged)
            ? node with { QueryOptions = options, Children = children }
            : node;
    }

    private static SemanticFilterExpression RewriteFilter(
        SemanticFilterExpression filter,
        ref bool changed)
    {
        switch (filter)
        {
            case SemanticAggregateFilter aggregate when IsEligible(aggregate):
            {
                changed = true;

                var quantifier = IsEmptyStrategy(aggregate)
                    ? SemanticRelationshipQuantifier.None
                    : SemanticRelationshipQuantifier.Some;

                return new SemanticRelationshipFilter(
                    aggregate.Relationship,
                    quantifier,
                    aggregate.Predicate!);
            }

            case SemanticAndFilter and:
            {
                var expressions = new SemanticFilterExpression[and.Expressions.Count];
                var nodeChanged = false;

                for (var i = 0; i < and.Expressions.Count; i++)
                {
                    var expression = RewriteFilter(
                        and.Expressions[i],
                        ref changed);

                    expressions[i] = expression;

                    if (!ReferenceEquals(expression, and.Expressions[i]))
                        nodeChanged = true;
                }

                if (!nodeChanged)
                    return filter;

                changed = true;
                return new SemanticAndFilter(expressions);
            }

            case SemanticOrFilter or:
            {
                var expressions = new SemanticFilterExpression[or.Expressions.Count];
                var nodeChanged = false;

                for (var i = 0; i < or.Expressions.Count; i++)
                {
                    var expression = RewriteFilter(
                        or.Expressions[i],
                        ref changed);

                    expressions[i] = expression;

                    if (!ReferenceEquals(expression, or.Expressions[i]))
                        nodeChanged = true;
                }

                if (!nodeChanged)
                    return filter;

                changed = true;
                return new SemanticOrFilter(expressions);
            }

            case SemanticRelationshipFilter relationship:
            {
                var predicate = RewriteFilter(
                    relationship.Predicate,
                    ref changed);

                if (ReferenceEquals(predicate, relationship.Predicate))
                    return filter;

                return relationship with
                {
                    Predicate = predicate
                };
            }

            default:
                return filter;
        }
    }

    private static bool ContainsEligible(SemanticPlanNode node) =>
        (node.QueryOptions?.Filter is not null && ContainsEligible(node.QueryOptions.Filter)) ||
        node.Children.Any(ContainsEligible);

    private static bool ContainsEligible(SemanticFilterExpression filter) =>
        filter switch
        {
            SemanticAggregateFilter aggregate => IsEligible(aggregate),
            SemanticAndFilter and => and.Expressions.Any(ContainsEligible),
            SemanticOrFilter or => or.Expressions.Any(ContainsEligible),
            SemanticRelationshipFilter relationship => ContainsEligible(relationship.Predicate),
            _ => false
        };

    private static bool IsEligible(SemanticAggregateFilter aggregate) =>
        aggregate.Aggregate == SemanticFilterAggregate.Count &&
        aggregate.Field is null &&
        aggregate.Predicate is not null &&
        AggregateExecutionStrategyResolver.Resolve(aggregate.Operator, aggregate.Value) is
            AggregateExecutionStrategy.CountExistsShortCircuit or
            AggregateExecutionStrategy.CountEmptyShortCircuit;

    private static bool IsEmptyStrategy(SemanticAggregateFilter aggregate) =>
        AggregateExecutionStrategyResolver.Resolve(aggregate.Operator, aggregate.Value) ==
        AggregateExecutionStrategy.CountEmptyShortCircuit;

    private static bool PredicateShapeMatches(SemanticPlan before, SemanticPlan after)
    {
        // The canonical fingerprint performs the stronger provider-neutral equivalence check.
        // This additional structural guard ensures the rule did not accidentally drop the
        // relationship predicate while changing only the outer quantifier.
        return CollectExistencePredicates(before.Root).OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(CollectExistencePredicates(after.Root).OrderBy(x => x, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private static IEnumerable<string> CollectExistencePredicates(SemanticPlanNode node)
    {
        if (node.QueryOptions?.Filter is not null)
        {
            foreach (var value in CollectExistencePredicates(node.QueryOptions.Filter))
                yield return value;
        }

        foreach (var child in node.Children)
        foreach (var value in CollectExistencePredicates(child))
            yield return value;
    }

    private static IEnumerable<string> CollectExistencePredicates(SemanticFilterExpression filter)
    {
        switch (filter)
        {
            case SemanticAggregateFilter aggregate when IsEligible(aggregate):
                yield return $"{aggregate.Relationship.Value}|{aggregate.Predicate}";
                break;
            case SemanticRelationshipFilter relationship:
                yield return $"{relationship.Relationship.Value}|{relationship.Predicate}";
                break;
            case SemanticAndFilter and:
                foreach (var child in and.Expressions)
                foreach (var nested in CollectExistencePredicates(child))
                    yield return nested;
                break;
            case SemanticOrFilter or:
                foreach (var child in or.Expressions)
                foreach (var nested in CollectExistencePredicates(child))
                    yield return nested;
                break;
        }
    }
}