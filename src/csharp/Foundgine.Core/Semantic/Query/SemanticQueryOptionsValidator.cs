using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Query;

/// <summary>
/// Validates query controls before provider planning. Provider-specific
/// limits remain provider policy; semantic invariants are enforced here.
/// </summary>
public static class SemanticQueryOptionsValidator
{
    public static void Validate(SemanticQueryOptions? options, SemanticEntity root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (options is null) return;

        if (options.Limit is < 0)
            throw new InvalidOperationException("Semantic query limit must be non-negative.");
        if (options.Offset is < 0)
            throw new InvalidOperationException("Semantic query offset must be non-negative.");
        if (options.After is not null && string.IsNullOrWhiteSpace(options.After))
            throw new InvalidOperationException("Semantic cursor cannot be empty.");
        if (options.After is not null && options.Offset is not null)
            throw new InvalidOperationException("Cursor pagination cannot be combined with offset pagination.");
        if (options.After is not null && options.Limit is not > 0)
            throw new InvalidOperationException("Cursor pagination requires a positive limit.");

        ValidateOrderTerms(options.EffectiveOrder, root);
    }

    private static void ValidateOrderTerms(IReadOnlyList<SemanticOrderTerm> terms, SemanticEntity root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in terms)
        {
            var key =
                $"{string.Join('/', term.EffectivePath.Select(x => x.Value))}:{term.Field.Value}:{term.Direction}:{term.Aggregate}";
            if (!seen.Add(key))
                throw new InvalidOperationException("Semantic query ordering contains a duplicate term.");
        }
    }
}