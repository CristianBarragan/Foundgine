using System.Globalization;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Single source of truth for whether a bare COUNT aggregate comparison (no target field,
/// no predicate) reduces to an emptiness/existence test — i.e. whether its truth value
/// depends only on whether the related collection is empty, not on the exact count.
///
/// <see cref="AggregateCardinalityOptimizationRule"/> uses this to decide what strategy hint
/// to attach to a plan node. Provider compilers (e.g. the SQL writer) use the exact same
/// derivation to decide whether an individual COUNT aggregate filter on that node still
/// matches the node's hint closely enough to be rendered as EXISTS / NOT EXISTS instead of a
/// scalar COUNT subquery comparison. Keeping both call sites on one implementation means the
/// hint a provider acts on can never silently drift from the definition that justified it.
/// </summary>
public static class AggregateExecutionStrategyResolver
{
    /// <summary>
    /// Returns the execution strategy this comparison reduces to, or <c>null</c> if the
    /// comparison genuinely depends on the exact count and cannot be short-circuited.
    /// </summary>
    public static AggregateExecutionStrategy? Resolve(SemanticAggregateFilterOperator op, object? value)
    {
        if (!TryGetIntegral(value, out var count))
            return null;

        // COUNT is non-negative. Only these exact thresholds collapse to a
        // pure emptiness/non-emptiness predicate. In particular, COUNT >= 0
        // and COUNT < 0 are constants and must not be treated as existence tests.
        return op switch
        {
            SemanticAggregateFilterOperator.Gt when count == 0 => AggregateExecutionStrategy.CountExistsShortCircuit,
            SemanticAggregateFilterOperator.Gte when count == 1 => AggregateExecutionStrategy.CountExistsShortCircuit,
            SemanticAggregateFilterOperator.Neq when count == 0 => AggregateExecutionStrategy.CountExistsShortCircuit,
            SemanticAggregateFilterOperator.Eq when count == 0 => AggregateExecutionStrategy.CountEmptyShortCircuit,
            SemanticAggregateFilterOperator.Lt when count == 1 => AggregateExecutionStrategy.CountEmptyShortCircuit,
            SemanticAggregateFilterOperator.Lte when count == 0 => AggregateExecutionStrategy.CountEmptyShortCircuit,
            _ => null
        };
    }

    /// <summary>
    /// Whether <paramref name="filter"/> is eligible for existence-style rendering under
    /// <paramref name="nodeStrategy"/> — i.e. it is a bare COUNT comparison (no field, no
    /// nested predicate) whose own comparison independently resolves to that same strategy.
    /// A node-level hint only licenses rewriting the specific aggregate filters that earned
    /// it; other aggregate filters sharing the node (e.g. ones with a target field) are left
    /// untouched even when the node carries a non-default strategy.
    /// </summary>
    public static bool IsEligibleFor(SemanticAggregateFilter filter, AggregateExecutionStrategy nodeStrategy)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (nodeStrategy == AggregateExecutionStrategy.Default)
            return false;

        if (filter.Aggregate != SemanticFilterAggregate.Count || filter.Field is not null ||
            filter.Predicate is not null)
            return false;

        return Resolve(filter.Operator, filter.Value) == nodeStrategy;
    }

    public static bool TryGetIntegral(object? value, out long result)
    {
        switch (value)
        {
            case byte v:
                result = v;
                return true;
            case sbyte v:
                result = v;
                return true;
            case short v:
                result = v;
                return true;
            case ushort v:
                result = v;
                return true;
            case int v:
                result = v;
                return true;
            case uint v:
                result = v;
                return true;
            case long v:
                result = v;
                return true;
            case ulong v when v <= long.MaxValue:
                result = (long)v;
                return true;
            case string v when long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}