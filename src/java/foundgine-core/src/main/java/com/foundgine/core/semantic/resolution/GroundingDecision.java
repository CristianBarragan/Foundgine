package com.foundgine.core.semantic.resolution;

import com.foundgine.core.semantic.AliasWeightEvidenceGate.AliasInterpretationEvidence;
import java.util.*;

/** First-class result of lexical grounding. A partial search never commits. */
public record GroundingDecision(
        String expression,
        GroundingOutcome outcome,
        GroundingInterpretation committed,
        List<GroundingInterpretation> competingInterpretations,
        String reason,
        List<SemanticLexicalCandidate> rootCandidates,
        GroundingBudgetLimit budgetLimit,
        List<GroundingInterpretation> partialInterpretationsAtCutoff,
        AliasInterpretationEvidence aliasEvidence,
        List<CandidateTruncation> truncationRisks) {
    public GroundingDecision {
        Objects.requireNonNull(expression, "expression");
        Objects.requireNonNull(outcome, "outcome");
        competingInterpretations = competingInterpretations == null ? List.of() : List.copyOf(competingInterpretations);
        rootCandidates = rootCandidates == null ? List.of() : List.copyOf(rootCandidates);
        budgetLimit = budgetLimit == null ? GroundingBudgetLimit.NONE : budgetLimit;
        partialInterpretationsAtCutoff = partialInterpretationsAtCutoff == null ? List.of() : List.copyOf(partialInterpretationsAtCutoff);
        truncationRisks = truncationRisks == null ? List.of() : List.copyOf(truncationRisks);
    }
    public GroundingDecision(String expression, GroundingOutcome outcome, GroundingInterpretation committed,
                             List<GroundingInterpretation> competing, String reason,
                             List<SemanticLexicalCandidate> roots) {
        this(expression, outcome, committed, competing, reason, roots, GroundingBudgetLimit.NONE, List.of(),
                committed == null ? null : committed.effectiveAliasEvidence(), List.of());
    }
    public boolean hadCompetingMeanings() { return !competingInterpretations.isEmpty(); }
    public List<GroundingInterpretation> effectivePartialInterpretationsAtCutoff() { return partialInterpretationsAtCutoff; }
    public List<CandidateTruncation> effectiveTruncationRisks() { return truncationRisks; }
}
