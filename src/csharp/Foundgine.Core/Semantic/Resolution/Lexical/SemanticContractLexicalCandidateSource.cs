namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// Small provider-neutral lexical candidate source backed only by the frozen
/// semantic contract. It is useful when a host wants lexical grounding without
/// introducing an external retrieval service. Physical storage is never consulted.
/// Hosts may replace it with pgvector, Elasticsearch, or another candidate source.
/// </summary>
public sealed class SemanticContractLexicalCandidateSource : ISemanticLexicalCandidateSource
{
    private readonly IReadOnlyList<SemanticLexiconEntry> _entries;

    public SemanticContractLexicalCandidateSource(SemanticContractSnapshot contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        _entries = SemanticLexiconProjection.Build(contract);
    }

    public IReadOnlyList<SemanticLexicalCandidate> Retrieve(SemanticLexicalRequest request) =>
        Retrieve(request, CancellationToken.None);

    public IReadOnlyList<SemanticLexicalCandidate> Retrieve(
        SemanticLexicalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return _entries
            .Where(x => request.EffectiveKinds.Contains(x.Kind))
            .Select(x => (Entry: x, Score: Score(request.Token, x)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.CanonicalName, StringComparer.OrdinalIgnoreCase)
            .Take(request.Limit)
            .Select(x => new SemanticLexicalCandidate(
                request.Token,
                x.Entry.Kind,
                x.Entry.CanonicalName,
                x.Score,
                x.Entry.EntityId,
                x.Entry.RelationshipId,
                x.Entry.FieldId,
                x.Entry.SourceEntityId,
                x.Entry.TargetEntityId,
                x.Entry.Value,
                [new ResolutionEvidence(
                    $"Semantic contract lexical match for '{request.Token}'.",
                    CandidateEvidenceKind.Alias,
                    x.Score)]))
            .ToArray();
    }

    private static double Score(string token, SemanticLexiconEntry entry)
    {
        var candidates = new[] { entry.CanonicalName, entry.SearchText }
            .Concat(entry.EffectiveAliases);

        return candidates
            .Select(x => Similarity(token, x))
            .DefaultIfEmpty(0)
            .Max();
    }

    private static double Similarity(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return 1d;

        if (right.Contains(left, StringComparison.OrdinalIgnoreCase) ||
            left.Contains(right, StringComparison.OrdinalIgnoreCase))
            return 0.85d;

        var distance = Levenshtein(left, right);
        var max = Math.Max(left.Length, right.Length);
        return max == 0 ? 1d : Math.Max(0d, 1d - ((double)distance / max));
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1));
            }
            previous = current;
        }
        return previous[b.Length];
    }
}
