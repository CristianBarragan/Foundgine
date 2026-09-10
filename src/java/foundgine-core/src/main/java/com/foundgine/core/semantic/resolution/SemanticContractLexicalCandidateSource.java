package com.foundgine.core.semantic.resolution;

import com.foundgine.core.semantic.*;
import java.util.stream.StreamSupport;
import java.util.*;

public final class SemanticContractLexicalCandidateSource implements ISemanticLexicalCandidateSource {
    private final List<SemanticLexiconEntry> entries;

    public SemanticContractLexicalCandidateSource(SemanticContractSnapshot contract) {
        Objects.requireNonNull(contract);
        entries = SemanticLexiconProjection.build(contract);
    }

    @Override
    public List<SemanticLexicalCandidate> retrieve(SemanticLexicalRequest request) {
        return retrieve(request, () -> false);
    }

    @Override
    public List<SemanticLexicalCandidate> retrieve(SemanticLexicalRequest request, ISemanticLexicalCandidateSource.CancellationToken cancellationToken) {
        Objects.requireNonNull(request);
        Objects.requireNonNull(cancellationToken);
        if (cancellationToken.isCancellationRequested()) throw new java.util.concurrent.CancellationException();

        return entries.stream()
            .filter(x -> request.effectiveKinds().contains(x.kind()))
            .map(x -> new ScoredEntry(x, score(request.token(), x)))
            .filter(x -> x.score() > 0)
            .sorted(Comparator.comparingDouble(ScoredEntry::score).reversed()
                .thenComparing(x -> x.entry().canonicalName(), String.CASE_INSENSITIVE_ORDER))
            .limit(request.limit())
            .map(x -> new SemanticLexicalCandidate(
                request.token(), x.entry().kind(), x.entry().canonicalName(), x.score(),
                x.entry().entityId(), x.entry().relationshipId(), x.entry().fieldId(),
                x.entry().sourceEntityId(), x.entry().targetEntityId(), x.entry().value(),
                List.of(new ResolutionEvidence(
                    "Semantic contract lexical match for '" + request.token() + "'.",
                    CandidateEvidenceKind.ALIAS, x.score()))))
            .toList();
    }

    private static double score(String token, SemanticLexiconEntry entry) {
        return StreamSupport.stream(
            java.util.stream.Stream.concat(
                java.util.stream.Stream.of(entry.canonicalName(), entry.searchText()),
                entry.effectiveAliases().stream()).spliterator(), false)
            .mapToDouble(x -> similarity(token, x)).max().orElse(0);
    }
    private static double similarity(String left, String right) {
        if (left.equalsIgnoreCase(right)) return 1.0;
        if (right.toLowerCase(Locale.ROOT).contains(left.toLowerCase(Locale.ROOT)) ||
            left.toLowerCase(Locale.ROOT).contains(right.toLowerCase(Locale.ROOT))) return 0.85;
        int distance = levenshtein(left, right);
        int max = Math.max(left.length(), right.length());
        return max == 0 ? 1.0 : Math.max(0, 1.0 - ((double)distance / max));
    }
    private static int levenshtein(String a, String b) {
        int[] previous = new int[b.length()+1];
        for (int i=1;i<=a.length();i++) {
            int[] current = new int[b.length()+1]; current[0]=i;
            for(int j=1;j<=b.length();j++)
                current[j]=Math.min(Math.min(current[j-1]+1,previous[j]+1),
                    previous[j-1]+(Character.toUpperCase(a.charAt(i-1))==Character.toUpperCase(b.charAt(j-1))?0:1));
            previous=current;
        }
        return previous[b.length()];
    }
    private record ScoredEntry(SemanticLexiconEntry entry, double score) {}
}
