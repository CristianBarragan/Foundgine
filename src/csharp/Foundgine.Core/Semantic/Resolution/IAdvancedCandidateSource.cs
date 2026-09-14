using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>Optional candidate-source extensions for richer semantic resolution.</summary>
public interface IAdvancedCandidateSource : ICandidateSource
{
    IReadOnlyList<IdentityCandidate> FindByCompositeIdentity(
        EntityId entityType,
        IReadOnlyDictionary<string, string> identityValues);

    IReadOnlyList<IdentityCandidate> FindByTemporalIdentity(
        EntityId entityType,
        string identityValue,
        DateTimeOffset asOf);
}