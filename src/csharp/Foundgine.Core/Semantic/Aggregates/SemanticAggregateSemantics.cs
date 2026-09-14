using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Aggregates;

/// <summary>
/// What an aggregate evaluates to when the underlying collection has zero rows.
/// </summary>
public enum SemanticEmptyCollectionResult : byte
{
    /// <summary>The aggregate evaluates to zero for an empty collection (e.g. COUNT).</summary>
    Zero,

    /// <summary>The aggregate evaluates to NULL for an empty collection (e.g. MIN, MAX).</summary>
    Null
}

/// <summary>
/// How an aggregate treats NULL values that appear among its inputs.
/// </summary>
public enum SemanticNullInputBehavior : byte
{
    /// <summary>The aggregate can never itself produce or be affected by a NULL input value
    /// (e.g. COUNT(*) counts rows, not values, so it is unaffected by NULL fields).</summary>
    NeverNull,

    /// <summary>NULL input values are ignored. The aggregate result is NULL only when no
    /// non-NULL value remains after NULLs are discarded (e.g. MIN, MAX).</summary>
    IgnoresNull
}

/// <summary>
/// Whether an aggregate's result can change when the input collection contains duplicate
/// rows/values that would otherwise be considered equivalent.
///
/// COUNT is duplicate sensitive: COUNT(R) changes if a duplicate row is added or removed.
/// MIN/MAX are duplicate insensitive: duplicate values do not change the minimum or maximum.
/// </summary>
public enum SemanticDuplicateSensitivity : byte
{
    Insensitive,
    Sensitive
}

/// <summary>
/// Whether a rewrite that substitutes one aggregate for another (or collapses an aggregate
/// against a relationship) additionally requires a proof about the cardinality of the
/// underlying relationship (e.g. "at most one row") before it can be considered safe.
///
/// This milestone does not perform cardinality-dependent rewrites. The flag exists so that
/// <see cref="AggregateRewriteLegality"/> can fail closed once such rewrites are introduced,
/// rather than silently assuming cardinality it was never given proof of.
/// </summary>
public enum SemanticCardinalityRequirement : byte
{
    /// <summary>No cardinality proof is required for this aggregate's own semantics.</summary>
    None,

    /// <summary>A rewrite involving this aggregate requires a proof of relationship cardinality.</summary>
    RequiresProof
}

/// <summary>
/// What is known, at rewrite time, about the cardinality of the relationship an aggregate is
/// evaluated over. This is supplied by the caller (planner/optimizer); the semantic layer never
/// infers it implicitly.
/// </summary>
public enum SemanticCardinalityKnowledge : byte
{
    /// <summary>Nothing is known about how many related rows can exist.</summary>
    Unknown,

    /// <summary>The relationship is proven to yield at most one related row.</summary>
    AtMostOne,

    /// <summary>The relationship may yield more than one related row.</summary>
    Unbounded
}

/// <summary>
/// The explicit, centralized semantic contract for a single aggregate function.
///
/// This is the single source of truth for empty-collection, NULL-input, and
/// duplicate-sensitivity behavior. Optimizer and provider code must consult this
/// contract (via <see cref="SemanticAggregateSemanticsCatalog"/>) instead of
/// independently re-deriving these rules, so that every rewrite rule and every
/// provider agrees on what each aggregate means at the edges.
/// </summary>
public sealed record SemanticAggregateSemantics(
    SemanticFilterAggregate Aggregate,
    SemanticEmptyCollectionResult EmptyCollectionResult,
    SemanticNullInputBehavior NullInputBehavior,
    SemanticDuplicateSensitivity DuplicateSensitivity,
    SemanticCardinalityRequirement CardinalityRequirement = SemanticCardinalityRequirement.None)
{
    public bool IsDuplicateSensitive => DuplicateSensitivity == SemanticDuplicateSensitivity.Sensitive;

    public bool RequiresCardinalityProof => CardinalityRequirement == SemanticCardinalityRequirement.RequiresProof;
}

/// <summary>
/// Central, machine-readable catalog of <see cref="SemanticAggregateSemantics"/> for every
/// aggregate the semantic layer understands. Optimizer rules and providers must look up
/// semantics here rather than hard-coding assumptions about empty/NULL/duplicate behavior.
/// </summary>
public static class SemanticAggregateSemanticsCatalog
{
    /// <summary>
    /// COUNT: zero for an empty collection, never itself NULL, and sensitive to duplicates
    /// (adding or removing a duplicate row changes the count).
    /// </summary>
    public static readonly SemanticAggregateSemantics Count = new(
        SemanticFilterAggregate.Count,
        SemanticEmptyCollectionResult.Zero,
        SemanticNullInputBehavior.NeverNull,
        SemanticDuplicateSensitivity.Sensitive);

    /// <summary>
    /// MIN: NULL for an empty collection, ignores NULL inputs (NULL only when no non-NULL
    /// value remains), and insensitive to duplicates.
    /// </summary>
    public static readonly SemanticAggregateSemantics Min = new(
        SemanticFilterAggregate.Min,
        SemanticEmptyCollectionResult.Null,
        SemanticNullInputBehavior.IgnoresNull,
        SemanticDuplicateSensitivity.Insensitive);

    /// <summary>
    /// MAX: NULL for an empty collection, ignores NULL inputs (NULL only when no non-NULL
    /// value remains), and insensitive to duplicates.
    /// </summary>
    public static readonly SemanticAggregateSemantics Max = new(
        SemanticFilterAggregate.Max,
        SemanticEmptyCollectionResult.Null,
        SemanticNullInputBehavior.IgnoresNull,
        SemanticDuplicateSensitivity.Insensitive);

    private static readonly IReadOnlyDictionary<SemanticFilterAggregate, SemanticAggregateSemantics> ByAggregate =
        new Dictionary<SemanticFilterAggregate, SemanticAggregateSemantics>
        {
            [SemanticFilterAggregate.Count] = Count,
            [SemanticFilterAggregate.Min] = Min,
            [SemanticFilterAggregate.Max] = Max
        };

    /// <summary>Every registered aggregate semantic contract, ordered by aggregate.</summary>
    public static IReadOnlyList<SemanticAggregateSemantics> All { get; } =
        [Count, Min, Max];

    /// <summary>
    /// Looks up the semantic contract for <paramref name="aggregate"/>.
    /// Throws if the aggregate has no registered contract, so that new aggregates can never
    /// be silently treated as COUNT/MIN/MAX-equivalent by omission.
    /// </summary>
    public static SemanticAggregateSemantics For(SemanticFilterAggregate aggregate) =>
        ByAggregate.TryGetValue(aggregate, out var semantics)
            ? semantics
            : throw new NotSupportedException(
                $"No semantic contract is registered for aggregate '{aggregate}'. " +
                "Register one in SemanticAggregateSemanticsCatalog before using it in a rewrite rule.");

    /// <summary>
    /// Attempts to look up the semantic contract for <paramref name="aggregate"/> without throwing.
    /// </summary>
    public static bool TryGet(SemanticFilterAggregate aggregate, out SemanticAggregateSemantics? semantics) =>
        ByAggregate.TryGetValue(aggregate, out semantics);
}
