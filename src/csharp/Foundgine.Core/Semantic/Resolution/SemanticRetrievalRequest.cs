using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// A provider-neutral request for approximate retrieval.
/// It describes what to retrieve, never how to search it.
/// </summary>
public sealed record SemanticRetrievalRequest
{
    public EntityId EntityType { get; }

    public FieldId? Field { get; }

    public string Query { get; }

    public RetrievalStrategy Strategy { get; }

    public int Limit { get; }

    public EntityId? SourceEntity { get; }

    public RelationshipId? Relationship { get; }

    public string? ReferenceIdentity { get; }

    public SemanticRetrievalRequest(
        EntityId entityType,
        FieldId? field,
        string query,
        RetrievalStrategy strategy,
        int limit = 10,
        EntityId? sourceEntity = null,
        RelationshipId? relationship = null,
        string? referenceIdentity = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException(
                "Retrieval query cannot be empty.",
                nameof(query));
        }

        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Retrieval limit must be between 1 and 1000.");
        }

        EntityType = entityType;
        Field = field;
        Query = query;
        Strategy = strategy;
        Limit = limit;
        SourceEntity = sourceEntity;
        Relationship = relationship;
        ReferenceIdentity = referenceIdentity;
    }
}

/// <summary>
/// A search result returned by an external retrieval provider.
/// The database remains the source of truth.
/// </summary>
public sealed record RetrievalCandidate(
    EntityId EntityType,
    string RecordId,
    double Score,
    FieldId? MatchedField = null,
    string? IdentityValue = null,
    IReadOnlyList<ResolutionEvidence>? Evidence = null,
    CandidateEvidenceKind? EvidenceKind = null)
{
    public IReadOnlyList<ResolutionEvidence> EffectiveEvidence =>
        Evidence ?? [];
}