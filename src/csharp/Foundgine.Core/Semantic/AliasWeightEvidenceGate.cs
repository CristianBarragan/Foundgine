using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Resolution;

namespace Foundgine.Core.Semantic;

/// <summary>Describes whether weighted alias evidence was applicable to a resolution.</summary>
public enum AliasEvidenceStatus : byte
{
    /// <summary>No lexical alias with a declared weight participated in the resolution.</summary>
    NotApplicable,

    /// <summary>Weighted alias evidence participated and all observed identities met the threshold.</summary>
    Sufficient,

    /// <summary>Weighted alias evidence participated and at least one observed identity was below the threshold.</summary>
    Insufficient
}

/// <summary>
/// Whether the semantic model itself was already known with certainty prior to
/// lexical resolution. This is a different epistemic category from declared
/// alias evidence: it describes provenance of the model, not the strength of a
/// lexical match, and it deliberately has no numeric scale so it can never be
/// combined arithmetically (e.g. max/averaged) with an alias weight.
/// </summary>
public enum ModelResolutionEvidence : byte
{
    /// <summary>The model's identity was not asserted as already known.</summary>
    Unknown,

    /// <summary>The model's identity was known with certainty independent of lexical alias matching.</summary>
    KnownWithCertainty
}

/// <summary>
/// Measured, application-declared lexical evidence. This type does not perform
/// authorization and does not alter retrieval scores.
/// </summary>
public sealed record AliasWeightEvidenceResult(
    AliasEvidenceStatus Status,
    ModelResolutionEvidence ModelEvidence,
    IReadOnlyDictionary<EntityId, int> EntityWeights,
    IReadOnlyDictionary<FieldId, int> FieldWeights,
    IReadOnlyDictionary<RelationshipId, int> RelationshipWeights,
    IReadOnlyList<EntityId> ViolatingEntities,
    IReadOnlyList<FieldId> ViolatingFields,
    IReadOnlyList<RelationshipId> ViolatingRelationships,
    string ContractFingerprint)
{
    /// <summary>
    /// Compatibility projection. Prefer <see cref="Status"/> because
    /// NotApplicable and Sufficient are intentionally different states.
    /// </summary>
    [Obsolete("Use Status. IsConclusive is true for both NotApplicable and Sufficient.", false)]
    public bool IsConclusive => Status != AliasEvidenceStatus.Insufficient;

    /// <summary>
    /// Compatibility projection onto the old numeric representation.
    /// </summary>
    [Obsolete("Use ModelEvidence. Model provenance is not on the same numeric scale as declared alias weight.", false)]
    public int? ModelWeight => ModelEvidence == ModelResolutionEvidence.KnownWithCertainty ? 100 : null;
}

/// <summary>
/// Measures application-declared alias weights that actually participated in
/// lexical grounding. The resolver may use this measurement as a commitment
/// policy, but the evaluator itself never authorizes execution.
/// </summary>
public static class AliasWeightEvidenceGate
{
    public static AliasWeightEvidenceResult Evaluate(
        SemanticModel model,
        int minimumWeight,
        SemanticLexicalResolution? lexicalResolution = null,
        bool modelKnownWithCertainty = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.EnsureFrozen();
        return Evaluate(model.CreateSnapshot(), minimumWeight, lexicalResolution, modelKnownWithCertainty);
    }

    public static AliasWeightEvidenceResult Evaluate(
        SemanticContractSnapshot model,
        int minimumWeight,
        SemanticLexicalResolution? lexicalResolution = null,
        bool modelKnownWithCertainty = false)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (minimumWeight is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(minimumWeight), minimumWeight,
                "minimumWeight must be between 1 and 100 (inclusive).");

        var modelEvidence = modelKnownWithCertainty
            ? ModelResolutionEvidence.KnownWithCertainty
            : ModelResolutionEvidence.Unknown;

        if (lexicalResolution is null || lexicalResolution.Steps.Count == 0)
        {
            return new(
                AliasEvidenceStatus.NotApplicable,
                modelEvidence,
                new Dictionary<EntityId, int>(),
                new Dictionary<FieldId, int>(),
                new Dictionary<RelationshipId, int>(),
                [],
                [],
                [],
                model.ContractFingerprint);
        }

        var entityWeights = new Dictionary<EntityId, int>();
        var fieldWeights = new Dictionary<FieldId, int>();
        var relationshipWeights = new Dictionary<RelationshipId, int>();

        foreach (var step in lexicalResolution.Steps)
        {
            var candidate = step.Candidate;
            switch (candidate.Kind)
            {
                case SemanticLexicalCandidateKind.Entity:
                case SemanticLexicalCandidateKind.Node:
                    if (candidate.EntityId is EntityId entityId &&
                        model.TryGetAlias(entityId, candidate.Token, out var entityAliasWeight))
                        AccumulateMax(entityWeights, entityId, entityAliasWeight);
                    break;

                case SemanticLexicalCandidateKind.Field:
                    if (candidate.EntityId is EntityId fieldOwnerEntityId &&
                        candidate.FieldId is FieldId fieldId &&
                        model.TryGetAlias(fieldOwnerEntityId, fieldId, candidate.Token, out var fieldAliasWeight))
                        AccumulateMax(fieldWeights, fieldId, fieldAliasWeight);
                    break;

                case SemanticLexicalCandidateKind.Relationship:
                    if (candidate.RelationshipId is RelationshipId relationshipId &&
                        model.TryGetAlias(relationshipId, candidate.Token, out var relationshipAliasWeight))
                        AccumulateMax(relationshipWeights, relationshipId, relationshipAliasWeight);
                    break;

                case SemanticLexicalCandidateKind.Traversal:
                    // Traversals are derived graph paths, not declared aliases.
                    break;
            }
        }

        var violatingEntities = entityWeights.Where(x => x.Value < minimumWeight).Select(x => x.Key).ToArray();
        var violatingFields = fieldWeights.Where(x => x.Value < minimumWeight).Select(x => x.Key).ToArray();
        var violatingRelationships =
            relationshipWeights.Where(x => x.Value < minimumWeight).Select(x => x.Key).ToArray();

        var applicable = entityWeights.Count > 0 || fieldWeights.Count > 0 || relationshipWeights.Count > 0;
        var status = !applicable
            ? AliasEvidenceStatus.NotApplicable
            : violatingEntities.Length == 0 &&
              violatingFields.Length == 0 &&
              violatingRelationships.Length == 0
                ? AliasEvidenceStatus.Sufficient
                : AliasEvidenceStatus.Insufficient;

        return new(
            status,
            modelEvidence,
            entityWeights,
            fieldWeights,
            relationshipWeights,
            violatingEntities,
            violatingFields,
            violatingRelationships,
            model.ContractFingerprint);
    }

    private static void AccumulateMax<TKey>(Dictionary<TKey, int> values, TKey key, int weight)
        where TKey : notnull
    {
        if (!values.TryGetValue(key, out var existing) || weight > existing)
            values[key] = weight;
    }
}

/// <summary>
/// Backing evidence attached to one grounding interpretation. Alias weights
/// are integers on their declared 1-100 scale; interpretation scores remain
/// separate doubles because they are retrieval/graph ranking heuristics.
/// ContractFingerprint identifies the exact frozen semantic contract this
/// evidence was measured against, so an audit trail can prove which contract
/// version a Sufficient/Insufficient verdict corresponds to.
/// </summary>
public sealed record AliasInterpretationEvidence(
    AliasEvidenceStatus Status,
    IReadOnlyDictionary<EntityId, int> EntityWeights,
    IReadOnlyDictionary<FieldId, int> FieldWeights,
    IReadOnlyDictionary<RelationshipId, int> RelationshipWeights,
    string? ContractFingerprint = null)
{
    public static AliasInterpretationEvidence From(AliasWeightEvidenceResult result) =>
        new(result.Status, result.EntityWeights, result.FieldWeights, result.RelationshipWeights,
            result.ContractFingerprint);
}