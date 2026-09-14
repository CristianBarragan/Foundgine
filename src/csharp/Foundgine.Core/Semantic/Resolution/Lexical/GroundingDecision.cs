using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// Outcome of a grounding decision. A grounding decision is not "did the
/// tokens map onto a legal path" — the graph-constrained search already
/// answers that. It is "how many distinct <em>meanings</em> were legal, and
/// is Foundgine justified in silently picking one of them."
/// </summary>
public enum GroundingOutcome : byte
{
    /// <summary>A single interpretation was constructed, or several
    /// interpretations were constructed but they agree on meaning (they
    /// differ only in the bridging route through the graph, not in what
    /// the expression maps to).</summary>
    Committed,

    /// <summary>Foundgine has a candidate meaning but must not silently commit
    /// it. This includes competing meanings within the ambiguity margin and,
    /// when configured, insufficient declared lexical alias evidence.</summary>
    RequiresClarification,

    /// <summary>No legal interpretation could be constructed at all.</summary>
    Unresolved,

    /// <summary>The search was stopped by a configured resource limit
    /// (token count, total paths explored, elapsed search time, retrieval
    /// time, or a cancelled <see cref="System.Threading.CancellationToken"/>)
    /// before it could finish exploring every candidate interpretation. This
    /// is a distinct outcome from <see cref="Unresolved"/>: it does not mean
    /// no legal path exists, it means Foundgine cannot yet prove there is
    /// only one. A partial search is not evidence of a single meaning, so
    /// this outcome never carries a <see cref="GroundingDecision.Committed"/>
    /// interpretation — it fails closed rather than authorizing whatever
    /// was found before the limit was hit.</summary>
    BudgetExceeded
}

/// <summary>Which configured resource limit stopped a search that ended in
/// <see cref="GroundingOutcome.BudgetExceeded"/>. Exposed so the limit that
/// was hit can be logged, alerted on, or tuned per deployment without
/// parsing <see cref="GroundingDecision.Reason"/> text.</summary>
public enum GroundingBudgetLimit : byte
{
    /// <summary>No limit was hit; not applicable outside <see cref="GroundingOutcome.BudgetExceeded"/>.</summary>
    None,

    /// <summary>The expression tokenized to more tokens than <c>maxTokens</c> allows.
    /// Checked before any search begins, so this is the cheapest limit to hit.</summary>
    MaxTokens,

    /// <summary>The search visited more candidate/path nodes than <c>maxPathsExplored</c>
    /// allows before it could finish enumerating every interpretation. Backtracking is
    /// not a separately budgeted limit — every backtrack is a DFS re-entry, so it
    /// consumes this same shared work budget.</summary>
    MaxPathsExplored,

    /// <summary>The in-memory graph search ran longer than the configured <c>timeout</c>
    /// before it could finish enumerating every interpretation. This clock starts only
    /// after candidate retrieval has completed — see <see cref="RetrievalTimeout"/> for
    /// the stage before it.</summary>
    Timeout,

    /// <summary>Candidate retrieval for one or more tokens exceeded the configured
    /// <c>retrievalTimeout</c>. Retrieval happens entirely before the in-memory search
    /// budget starts counting, so this is a distinct limit from <see cref="Timeout"/> —
    /// it guards against a slow or hung candidate source (network partition, slow
    /// index), which none of the search-time bounds can see.</summary>
    RetrievalTimeout,

    /// <summary>The caller-supplied <see cref="System.Threading.CancellationToken"/>
    /// was cancelled before grounding finished, whether during retrieval or search.</summary>
    Cancelled
}

/// <summary>
/// One candidate meaning for a lexical expression: the token-by-token
/// mapping onto the semantic contract it commits to, its interpretation score, and
/// a signature that identifies what it means as distinct from how it got
/// there. Two interpretations that reach the same relationship, field, or
/// value via different bridging routes share a <see cref="Signature"/>;
/// two interpretations that map a token to a different field, value,
/// relationship, or root entity do not, even if their graph paths are
/// both legal.
/// </summary>
public sealed record GroundingInterpretation(
    IReadOnlyList<SemanticLexicalStep> Steps,
    double InterpretationScore,
    EntityId RootEntity,
    string Signature,
    AliasInterpretationEvidence? AliasEvidence = null)
{
    /// <summary>Compatibility alias for callers migrating from the pre-2.0.1 name. The value is a heuristic
    /// interpretation score, not calibrated confidence.</summary>
    [Obsolete("Use InterpretationScore. This value is a heuristic ranking score, not calibrated confidence.", false)]
    public double Confidence => InterpretationScore;

    /// <summary>Lexical and graph-similarity evidence backing this interpretation,
    /// one group per step, in expression order. This is the evidence a person or
    /// an authorization log can inspect to see *why* this meaning was chosen —
    /// not just that a path existed.</summary>
    public IEnumerable<ResolutionEvidence> LexicalEvidence => Steps.SelectMany(x => x.Candidate.EffectiveEvidence);

    /// <summary>Application-declared alias evidence used by the optional commitment policy.
    /// Null is reserved for callers constructing the compatibility record directly.</summary>
    public AliasInterpretationEvidence EffectiveAliasEvidence =>
        AliasEvidence ?? new(AliasEvidenceStatus.NotApplicable, new Dictionary<EntityId, int>(),
            new Dictionary<FieldId, int>(), new Dictionary<RelationshipId, int>());
}

/// <summary>
/// First-class record of how a lexical <param name="Expression"/> was grounded against the
/// frozen semantic contract: what it was committed to (if anything), what
/// else it could plausibly have meant, and whether that plurality of
/// meaning is material enough to block automatic commitment.
/// 
/// This exists because graph-constrained retrieval answers a narrower
/// question than grounding does. A candidate that fits the semantic graph
/// is not necessarily the meaning the user intended; a high retrieval
/// score is not meaning; a semantically valid path is not proof that it
/// is the intended path. <see>
///     <cref>SemanticLexicalResolver.Ground</cref>
/// </see>
/// surfaces every structurally valid, semantically distinct interpretation
/// instead of quietly returning the top-ranked one, so ambiguity can be
/// inspected, logged, or escalated to a clarifying question rather than
/// collapsed into a single authorized — and possibly wrong — execution.
/// </summary>
/// <param name="BudgetLimit">Which resource limit fired, when <param name="Outcome"/>
/// is <see cref="GroundingOutcome.BudgetExceeded"/>; <see cref="GroundingBudgetLimit.None"/>
/// otherwise.</param>
/// <remarks>Competing interpretations are internal semantic results. Applications must not expose their
/// semantic metadata directly to untrusted callers unless that metadata is disclosure-safe; prefer a
/// sanitized clarification projection at the runtime/application boundary.</remarks>
/// <param name="PartialInterpretationsAtCutoff">Populated only when <paramref name="Outcome"/>
/// is <see cref="GroundingOutcome.BudgetExceeded"/>: whatever semantically distinct
/// interpretations the search had already constructed at the moment the limit fired.
/// Diagnostic only — useful for logging, alerting, or deciding whether to raise a budget
/// and retry. A partial search is not proof of a unique legal meaning, so this list is
/// never treated as authorizable: <param name="Committed"/> stays null no matter how many
/// entries it has, and nothing in Foundgine executes against it <param name="CompetingInterpretations"/>
/// <param name="Reason"/><param name="RootCandidates"/>.</param>
/// <param name="AliasEvidence">Application-declared alias evidence backing the leading
/// interpretation, when <paramref name="Outcome"/> is <see cref="GroundingOutcome.Committed"/>
/// or <see cref="GroundingOutcome.RequiresClarification"/> due to insufficient weight. Null
/// when the optional alias-weight commitment policy was not configured or did not apply.</param>
/// <param name="TruncationRisks">Populated when a token used by the leading interpretation had its
/// candidate set cut down to <c>candidateLimit</c> at retrieval time, and the highest-scoring candidate
/// that was cut fell within the resolver's ambiguity margin of the lowest candidate that was kept
/// (see <see cref="CandidateTruncation.WithinAmbiguityMargin"/>). Retrieval truncation happens before
/// graph search runs, so a cut candidate is never checked for legality — it is simply absent, with no
/// record of whether it would have produced a competing meaning. When this is non-empty,
/// <paramref name="Outcome"/> is <see cref="GroundingOutcome.RequiresClarification"/> even though graph
/// search itself found no competing interpretation: the same "do not silently pick a winner inside the
/// margin" policy that governs surviving interpretations is applied here to a candidate that never got
/// the chance to survive or fail on its merits. Empty (never null) when no such risk was found.</param>
public sealed record GroundingDecision(
    string Expression,
    GroundingOutcome Outcome,
    GroundingInterpretation? Committed,
    IReadOnlyList<GroundingInterpretation> CompetingInterpretations,
    string Reason,
    IReadOnlyList<SemanticLexicalCandidate> RootCandidates,
    GroundingBudgetLimit BudgetLimit = GroundingBudgetLimit.None,
    IReadOnlyList<GroundingInterpretation>? PartialInterpretationsAtCutoff = null,
    AliasInterpretationEvidence? AliasEvidence = null,
    IReadOnlyList<CandidateTruncation>? TruncationRisks = null)
{
    /// <summary>True when more than one semantically distinct interpretation was
    /// structurally legal, regardless of whether Foundgine still committed to one
    /// (because one interpretation dominated on interpretation score) or refused to
    /// (because two or more remained within the ambiguity margin).</summary>
    public bool HadCompetingMeanings => CompetingInterpretations.Count > 0;

    /// <summary>Null-safe accessor for <see cref="PartialInterpretationsAtCutoff"/>.</summary>
    public IReadOnlyList<GroundingInterpretation> EffectivePartialInterpretationsAtCutoff =>
        PartialInterpretationsAtCutoff ?? [];

    /// <summary>Null-safe accessor for <see cref="TruncationRisks"/>.</summary>
    public IReadOnlyList<CandidateTruncation> EffectiveTruncationRisks => TruncationRisks ?? [];
}