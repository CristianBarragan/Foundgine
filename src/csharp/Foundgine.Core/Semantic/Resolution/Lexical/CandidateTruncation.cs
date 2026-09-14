namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// Records that a token's candidate set was cut down to <c>candidateLimit</c>
/// before graph search ever ran, and how close the cut was. This is a
/// retrieval-boundary event, not a search-time one: it can happen even for an
/// expression that ultimately grounds to a single, uncontested meaning,
/// because the candidate(s) it hid were never given a chance to reach graph
/// search and prove themselves illegal (or legal).
/// </summary>
/// <param name="Token">The lexical token whose candidate set was truncated.</param>
/// <param name="RetainedCount">How many candidates for this token were kept and handed to graph search.</param>
/// <param name="TruncatedCount">How many candidates for this token were retrieved but discarded purely
/// because they ranked below <c>candidateLimit</c>. These were never evaluated for graph legality.</param>
/// <param name="LowestRetainedScore">The retrieval score of the lowest-ranked candidate that was kept.</param>
/// <param name="HighestTruncatedScore">The retrieval score of the highest-ranked candidate that was cut.
/// Compared against <see cref="LowestRetainedScore"/>, this is the same kind of score gap the resolver
/// already uses to decide whether two *surviving* interpretations are within its ambiguity margin — applied
/// here to a candidate that was removed before it ever got that chance.</param>
public sealed record CandidateTruncation(
    string Token,
    int RetainedCount,
    int TruncatedCount,
    double LowestRetainedScore,
    double HighestTruncatedScore)
{
    /// <summary>The retrieval-score gap between the last candidate kept and the first candidate cut.
    /// A small gap means the truncation boundary fell in the middle of a cluster of similarly-scored
    /// candidates rather than after a clear drop-off, which is exactly the situation where a cut
    /// candidate could plausibly have represented a distinct, legal meaning.</summary>
    public double MarginGap => LowestRetainedScore - HighestTruncatedScore;

    /// <summary>True when <see cref="MarginGap"/> is smaller than the resolver's configured
    /// <c>ambiguityThreshold</c> — i.e. the cut candidate scored close enough to the retained ones that
    /// truncation, not graph-legality, may be the only reason it isn't part of the result.</summary>
    public bool WithinAmbiguityMargin(double ambiguityThreshold) => MarginGap < ambiguityThreshold;
}
