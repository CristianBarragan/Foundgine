using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// The boundary through which real data may participate in semantic
/// resolution. Implementations live outside the semantic layer.
/// </summary>
public interface ICandidateSource
{
    IReadOnlyList<IdentityCandidate> FindByIdentity(
        EntityId entityType,
        string identityValue);

    IReadOnlyList<IdentityCandidate> FindByRelationship(
        RelationshipId relationshipId,
        string sourceIdentityValue);
}