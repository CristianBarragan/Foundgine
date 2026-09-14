using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Resolution;

public enum SemanticLexicalResolutionOutcome : byte
{
    Resolved,
    Ambiguous,
    Unresolved,

    /// <summary>A configured resource limit (token count, paths explored,
    /// elapsed search time, or retrieval time) stopped grounding before it
    /// could prove there was only one legal interpretation — or the
    /// caller-supplied <see cref="System.Threading.CancellationToken"/> was
    /// cancelled. Callers must treat this the same as a stop signal, not as
    /// "resolved to the best candidate found so far."</summary>
    BudgetExceeded
}

/// <summary>One selected lexical interpretation and its graph path.</summary>
public sealed record SemanticLexicalStep(
    string Token,
    SemanticLexicalCandidate Candidate,
    double PathScore,
    IReadOnlyList<SemanticLexicalCandidate> BridgingPath);

/// <summary>Result of schema-constrained lexical inference.</summary>
public sealed record SemanticLexicalResolution(
    SemanticLexicalResolutionOutcome Outcome,
    IReadOnlyList<SemanticLexicalStep> Steps,
    double Confidence,
    EntityId? RootEntity,
    string? Reason,
    IReadOnlyList<SemanticLexicalCandidate>? RootCandidates = null)
{
    public IReadOnlyList<SemanticLexicalCandidate> EffectiveRootCandidates => RootCandidates ?? [];
}
