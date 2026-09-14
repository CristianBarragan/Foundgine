using Foundgine.Core.Semantic.Security;

namespace Foundgine.Core.Execution;

/// <summary>
/// Provider conformance evidence. This name deliberately distinguishes a
/// checked contract result from a mathematical proof of implementation safety.
/// </summary>
public sealed record SecurityInvariantAttestation(
    string Provider,
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Preserved,
    IReadOnlyList<string> Missing)
{
    public bool IsSatisfied => Missing.Count == 0;

    public void EnsureSatisfied()
    {
        if (!IsSatisfied)
            throw new InvalidOperationException(
                $"Provider '{Provider}' cannot satisfy required security invariants: {string.Join(", ", Missing)}.");
    }

    public static SecurityInvariantAttestation Create(
        string provider,
        IEnumerable<string> required,
        IEnumerable<string> preserved)
    {
        var requiredSet = required.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var preservedSet = preserved.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var missing = requiredSet.Except(preservedSet, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        return new SecurityInvariantAttestation(provider, requiredSet, preservedSet, missing);
    }
}