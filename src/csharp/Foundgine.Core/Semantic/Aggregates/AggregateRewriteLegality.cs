using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Aggregates;

/// <summary>
/// The outcome of a legality check performed by <see cref="AggregateRewriteLegality"/>.
///
/// A result is only <see cref="IsLegal"/> when every individual check it represents passed.
/// <see cref="Violations"/> is always empty when <see cref="IsLegal"/> is <see langword="true"/>,
/// and always non-empty when it is <see langword="false"/> — callers can rely on this instead of
/// re-deriving why a rewrite failed.
/// </summary>
public sealed record AggregateRewriteLegalityResult(
    bool IsLegal,
    IReadOnlyList<string> Violations)
{
    public static AggregateRewriteLegalityResult Legal { get; } = new(true, []);

    public static AggregateRewriteLegalityResult Illegal(params string[] violations)
    {
        if (violations.Length == 0)
            throw new ArgumentException("An illegal result must carry at least one violation.", nameof(violations));

        return new AggregateRewriteLegalityResult(false, violations);
    }

    /// <summary>
    /// Combines several results into one. The combined result is legal only if every input
    /// result was legal; otherwise it carries the union of all violations, in order.
    /// </summary>
    public static AggregateRewriteLegalityResult Combine(params AggregateRewriteLegalityResult[] results)
    {
        var violations = results
            .Where(r => !r.IsLegal)
            .SelectMany(r => r.Violations)
            .ToArray();

        return violations.Length == 0 ? Legal : new AggregateRewriteLegalityResult(false, violations);
    }
}

/// <summary>
/// The explicit legality boundary for aggregate rewrites.
///
/// This type does not perform any rewriting itself. It exists so that a rewrite rule can ask,
/// before it fires, "is it even semantically legal to substitute aggregate A for aggregate B
/// here?" — and get a fail-closed answer backed by the centralized
/// <see cref="SemanticAggregateSemanticsCatalog"/> contract rather than an ad-hoc,
/// rule-local assumption.
///
/// A rewrite that changes which aggregate function is evaluated must be rejected unless it
/// preserves:
/// <list type="bullet">
///   <item>empty-collection semantics (does the result differ when the collection is empty?)</item>
///   <item>NULL-input semantics (does NULL-handling differ?)</item>
///   <item>duplicate sensitivity (does the result differ for duplicate rows/values?)</item>
///   <item>cardinality requirements (does the rewrite depend on relationship cardinality that
///     was never proven?)</item>
/// </list>
/// The classic example this rejects: COUNT(R) → MIN(R.X). Both can appear to "check whether R
/// has rows" for some callers, but they disagree on all three of empty-collection result,
/// NULL-input behavior, and duplicate sensitivity, so the substitution is illegal.
/// </summary>
public static class AggregateRewriteLegality
{
    /// <summary>
    /// Checks whether the empty-collection result is preserved by substituting
    /// <paramref name="to"/> for <paramref name="from"/>.
    /// </summary>
    public static AggregateRewriteLegalityResult CheckEmptySemantics(
        SemanticAggregateSemantics from,
        SemanticAggregateSemantics to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return from.EmptyCollectionResult == to.EmptyCollectionResult
            ? AggregateRewriteLegalityResult.Legal
            : AggregateRewriteLegalityResult.Illegal(
                $"empty-collection semantics differ: '{from.Aggregate}' yields " +
                $"{Describe(from.EmptyCollectionResult)} for an empty collection, but " +
                $"'{to.Aggregate}' yields {Describe(to.EmptyCollectionResult)}.");
    }

    /// <summary>
    /// Checks whether NULL-input behavior is preserved by substituting <paramref name="to"/>
    /// for <paramref name="from"/>.
    /// </summary>
    public static AggregateRewriteLegalityResult CheckNullSemantics(
        SemanticAggregateSemantics from,
        SemanticAggregateSemantics to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return from.NullInputBehavior == to.NullInputBehavior
            ? AggregateRewriteLegalityResult.Legal
            : AggregateRewriteLegalityResult.Illegal(
                $"NULL-input semantics differ: '{from.Aggregate}' is {Describe(from.NullInputBehavior)}, " +
                $"but '{to.Aggregate}' is {Describe(to.NullInputBehavior)}.");
    }

    /// <summary>
    /// Checks whether duplicate sensitivity is preserved by substituting <paramref name="to"/>
    /// for <paramref name="from"/>.
    /// </summary>
    public static AggregateRewriteLegalityResult CheckDuplicateSensitivity(
        SemanticAggregateSemantics from,
        SemanticAggregateSemantics to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return from.IsDuplicateSensitive == to.IsDuplicateSensitive
            ? AggregateRewriteLegalityResult.Legal
            : AggregateRewriteLegalityResult.Illegal(
                $"duplicate sensitivity differs: '{from.Aggregate}' is " +
                $"{(from.IsDuplicateSensitive ? "duplicate-sensitive" : "duplicate-insensitive")}, but " +
                $"'{to.Aggregate}' is {(to.IsDuplicateSensitive ? "duplicate-sensitive" : "duplicate-insensitive")}.");
    }

    /// <summary>
    /// Checks whether the rewrite's cardinality requirements are satisfied.
    ///
    /// If either aggregate's contract requires a cardinality proof, the rewrite is only legal
    /// when <paramref name="knowledge"/> is something other than
    /// <see cref="SemanticCardinalityKnowledge.Unknown"/>. This fails closed: a rewrite that
    /// needs a cardinality proof is never assumed legal just because no proof was supplied.
    /// </summary>
    public static AggregateRewriteLegalityResult CheckCardinalityRequirement(
        SemanticAggregateSemantics from,
        SemanticAggregateSemantics to,
        SemanticCardinalityKnowledge knowledge)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        if (!from.RequiresCardinalityProof && !to.RequiresCardinalityProof)
            return AggregateRewriteLegalityResult.Legal;

        return knowledge != SemanticCardinalityKnowledge.Unknown
            ? AggregateRewriteLegalityResult.Legal
            : AggregateRewriteLegalityResult.Illegal(
                $"cardinality proof is required to substitute '{to.Aggregate}' for '{from.Aggregate}', " +
                "but the relationship cardinality is unknown at rewrite time.");
    }

    /// <summary>
    /// The full legality gate for substituting aggregate <paramref name="to"/> for aggregate
    /// <paramref name="from"/>. Runs every individual check and fails closed: the substitution
    /// is legal only when empty-collection semantics, NULL semantics, duplicate sensitivity,
    /// and cardinality requirements are all preserved.
    ///
    /// Substituting an aggregate for itself is always legal.
    /// </summary>
    public static AggregateRewriteLegalityResult CheckSubstitution(
        SemanticAggregateSemantics from,
        SemanticAggregateSemantics to,
        SemanticCardinalityKnowledge knowledge = SemanticCardinalityKnowledge.Unknown)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        if (from.Aggregate == to.Aggregate)
            return AggregateRewriteLegalityResult.Legal;

        return AggregateRewriteLegalityResult.Combine(
            CheckEmptySemantics(from, to),
            CheckNullSemantics(from, to),
            CheckDuplicateSensitivity(from, to),
            CheckCardinalityRequirement(from, to, knowledge));
    }

    /// <summary>
    /// Convenience overload that looks up both sides in
    /// <see cref="SemanticAggregateSemanticsCatalog"/> before running
    /// <see cref="CheckSubstitution(SemanticAggregateSemantics, SemanticAggregateSemantics, SemanticCardinalityKnowledge)"/>.
    /// </summary>
    public static AggregateRewriteLegalityResult CheckSubstitution(
        SemanticFilterAggregate from,
        SemanticFilterAggregate to,
        SemanticCardinalityKnowledge knowledge = SemanticCardinalityKnowledge.Unknown) =>
        CheckSubstitution(
            SemanticAggregateSemanticsCatalog.For(from),
            SemanticAggregateSemanticsCatalog.For(to),
            knowledge);

    private static string Describe(SemanticEmptyCollectionResult result) => result switch
    {
        SemanticEmptyCollectionResult.Zero => "zero",
        SemanticEmptyCollectionResult.Null => "NULL",
        _ => result.ToString()
    };

    private static string Describe(SemanticNullInputBehavior behavior) => behavior switch
    {
        SemanticNullInputBehavior.NeverNull => "never NULL",
        SemanticNullInputBehavior.IgnoresNull => "NULL-ignoring",
        _ => behavior.ToString()
    };
}
