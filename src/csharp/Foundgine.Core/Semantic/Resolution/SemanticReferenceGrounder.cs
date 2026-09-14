using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// Grounds a natural-language reference against the semantic contract before
/// data retrieval. Entity/field aliases narrow the search space; provider-backed
/// retrieval then contributes ranked data evidence. No provider-specific search
/// technology leaks into this layer.
/// </summary>
public sealed class SemanticReferenceGrounder
{
    private readonly SemanticModel _model;
    private readonly IApproximateCandidateSource _candidates;

    public SemanticReferenceGrounder(SemanticModel model, IApproximateCandidateSource candidates)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
    }

    public IReadOnlyList<SemanticReferenceEvidence> Ground(
        string query,
        RetrievalStrategy strategy = RetrievalStrategy.Search,
        int entityLimit = 5,
        int candidateLimit = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Reference query cannot be empty.", nameof(query));

        var entityMatches = _model.Entities
            .Select(entity =>
            {
                var entityScore = Similarity(query, entity.Name);
                var aliasScore = entity.EffectiveAliases
                    .Select(alias => Similarity(query, alias.Name))
                    .DefaultIfEmpty(0d)
                    .Max();
                var fieldScore = entity.Fields
                    .SelectMany(field => new[] { field.Name }.Concat(field.EffectiveAliases.Select(a => a.Name)))
                    .Select(name => Similarity(query, name))
                    .DefaultIfEmpty(0d)
                    .Max();
                return (Entity: entity, Score: Math.Max(entityScore, Math.Max(aliasScore, fieldScore * 0.9d)));
            })
            .Where(x => x.Entity.Fields.Any(field =>
                field.ClrType == typeof(string) && !field.Capabilities.HasFlag(SemanticFieldCapabilities.Sensitive)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entity.Name, StringComparer.OrdinalIgnoreCase)
            .Take(entityLimit)
            .ToArray();

        var results = new List<SemanticReferenceEvidence>();
        foreach (var match in entityMatches)
        {
            var field = SelectSearchField(match.Entity, query);
            if (field is null)
                continue;

            var retrieval = _candidates.Retrieve(new SemanticRetrievalRequest(
                match.Entity.Id,
                field.Id,
                query,
                strategy,
                candidateLimit));

            if (retrieval.Count == 0)
                continue;

            var confidence = Math.Clamp(
                (match.Score * 0.35d) + (retrieval[0].Score * 0.65d),
                0d,
                1d);

            results.Add(new SemanticReferenceEvidence(
                query,
                retrieval,
                confidence,
                $"'{query}' grounded against semantic entity '{match.Entity.Name}' and field '{field.Name}'."));
        }

        return results
            .OrderByDescending(x => x.Confidence)
            .ToArray();
    }

    private static SemanticField? SelectSearchField(SemanticEntity entity, string query) =>
        entity.Fields
            .Where(field => field.ClrType == typeof(string) &&
                            !field.Capabilities.HasFlag(SemanticFieldCapabilities.Sensitive))
            .Select(field =>
            {
                var score = 0.5d + (0.5d * Math.Max(
                    Similarity(query, field.Name),
                    field.EffectiveAliases.Select(alias => Similarity(query, alias.Name)).DefaultIfEmpty(0d).Max()));
                return (Field: field, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Field.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Field)
            .FirstOrDefault();

    private static double Similarity(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return 1d;
        if (left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
            right.Contains(left, StringComparison.OrdinalIgnoreCase))
            return 0.8d;

        var distance = Levenshtein(left, right);
        var max = Math.Max(left.Length, right.Length);
        return max == 0 ? 1d : 1d - ((double)distance / max);
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1));
            previous = current;
        }

        return previous[b.Length];
    }
}