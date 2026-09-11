using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// Minimal resolver port from V1. It preserves the proven rule that
/// resolution never invents an identity: zero is NotFound and multiple
/// candidates are Ambiguous.
/// </summary>
public sealed class EntityResolver
{
    private readonly SemanticModel _model;
    private readonly ICandidateSource _candidates;

    public EntityResolver(SemanticModel model, ICandidateSource candidates)
    {
        _model = model;
        _candidates = candidates;
    }

    public ResolutionResult ResolveByIdentity(
        EntityId entityType,
        string identityLiteral)
    {
        var entity = _model.Get(entityType);
        var matches = _candidates.FindByIdentity(entityType, identityLiteral);

        var evidence = new[]
        {
            new ResolutionEvidence(
                $"Looked up {entity.Name}.{entity.Identity.Name} = '{identityLiteral}': {matches.Count} match(es).")
        };

        if (matches.Count == 0)
            return ResolutionResult.NotFound(
                $"No {entity.Name} found with {entity.Identity.Name} '{identityLiteral}'.",
                evidence);

        if (matches.Count > 1)
            return ResolutionResult.Ambiguous(
                $"{matches.Count} {entity.Name} records matched identity '{identityLiteral}'.",
                evidence);

        var match = matches[0];

        return ResolutionResult.Success(new ResolvedReference(
            entityType,
            match.IdentityValue,
            1.0,
            $"Explicit identity matched {entity.Name}.{entity.Identity.Name}.",
            evidence));
    }

    public ResolutionResult ResolveByRelationship(
        ResolvedReference source,
        string relationshipName)
    {
        var sourceEntity = _model.Get(source.EntityType);

        var relationship = sourceEntity.Relationships.FirstOrDefault(r =>
            string.Equals(r.Name, relationshipName, StringComparison.OrdinalIgnoreCase));

        if (relationship is null)
            return ResolutionResult.NotFound(
                $"{sourceEntity.Name} has no relationship named '{relationshipName}'.",
                [
                    new ResolutionEvidence(
                        $"No relationship '{relationshipName}' declared on {sourceEntity.Name}.")
                ]);

        var matches = _candidates.FindByRelationship(
            relationship.Id,
            source.IdentityValue);

        var target = _model.Get(relationship.Target);

        var evidence = new[]
        {
            new ResolutionEvidence(
                $"Traversed {sourceEntity.Name}.{relationship.Name}: {matches.Count} candidate(s).")
        };

        if (matches.Count == 0)
            return ResolutionResult.NotFound(
                $"No {target.Name} found via {sourceEntity.Name}.{relationship.Name}.",
                evidence);

        if (matches.Count > 1)
            return ResolutionResult.Ambiguous(
                $"{matches.Count} {target.Name} found via {sourceEntity.Name}.{relationship.Name}.",
                evidence);

        return ResolutionResult.Success(new ResolvedReference(
            relationship.Target,
            matches[0].IdentityValue,
            0.9,
            $"Uniquely resolved via {sourceEntity.Name}.{relationship.Name}.",
            evidence));
    }

    /// <summary>Resolves an entity using its semantic name/alias before performing the identity lookup.</summary>
    public ResolutionResult ResolveBySemanticIdentity(
        string entityName,
        string identityLiteral)
    {
        if (!_model.TryResolveEntity(entityName, out var entity))
        {
            return ResolutionResult.NotFound(
                $"No semantic entity named '{entityName}' is defined.",
                [new ResolutionEvidence($"Semantic entity '{entityName}' was not found in the model.")]);
        }

        return ResolveByIdentity(entity.Id, identityLiteral);
    }

    /// <summary>Resolves an open semantic traversal by applying each declared relationship hop in order.</summary>
    public ResolutionResult ResolveByTraversal(
        ResolvedReference source,
        string traversalName)
    {
        var traversal = _model.GetTraversal(source.EntityType, traversalName);
        var current = source;
        var evidence = new List<ResolutionEvidence>();

        foreach (var relationshipId in traversal.Path)
        {
            var entity = _model.Get(current.EntityType);
            var relationship = entity.Relationships.FirstOrDefault(x => x.Id == relationshipId);
            if (relationship is null)
                return ResolutionResult.NotFound(
                    $"Traversal '{traversalName}' contains an undeclared relationship '{relationshipId}'.",
                    evidence);

            var result = ResolveByRelationship(current, relationship.Name);
            evidence.AddRange(result.Evidence);
            if (result.Outcome != ResolutionOutcome.Resolved)
                return result with { };
            current = result.Resolved!;
        }

        return ResolutionResult.Success(current with { Evidence = evidence });
    }

    /// <summary>Resolves a composite identity when the candidate source explicitly supports it.</summary>
    public ResolutionResult ResolveByCompositeKey(
        EntityId entityType,
        IReadOnlyDictionary<string, string> identityValues)
    {
        if (_candidates is not IAdvancedCandidateSource advanced)
            throw new NotSupportedException(
                "The configured candidate source does not support composite identity resolution.");
        var entity = _model.Get(entityType);
        var matches = advanced.FindByCompositeIdentity(entityType, identityValues);
        var evidence = new[]
            { new ResolutionEvidence($"Looked up composite identity on {entity.Name}: {matches.Count} match(es).") };
        return ResolveCandidates(entityType, entity.Name, matches, evidence, "composite identity");
    }

    /// <summary>Resolves an identity at a specific point in time when the candidate source supports temporal identity.</summary>
    public ResolutionResult ResolveByTemporalIdentity(
        EntityId entityType,
        string identityLiteral,
        DateTimeOffset asOf)
    {
        if (_candidates is not IAdvancedCandidateSource advanced)
            throw new NotSupportedException(
                "The configured candidate source does not support temporal identity resolution.");
        var entity = _model.Get(entityType);
        var matches = advanced.FindByTemporalIdentity(entityType, identityLiteral, asOf);
        var evidence = new[]
        {
            new ResolutionEvidence(
                $"Looked up {entity.Name}.{entity.Identity.Name} at {asOf:O}: {matches.Count} match(es).")
        };
        return ResolveCandidates(entityType, entity.Name, matches, evidence, "temporal identity");
    }

    /// <summary>Returns the closest semantic entity/relationship name match without inventing a data identity.</summary>
    public ResolutionResult ResolveByFuzzyMatch(string entityOrRelationshipName, EntityId? sourceEntity = null)
    {
        var names = sourceEntity is { } source
            ? _model.Get(source).Relationships.Select(x => x.Name).ToArray()
            : _model.Entities.Select(x => x.Name).ToArray();

        var match = names
            .Select(name => (Name: name, Distance: Levenshtein(entityOrRelationshipName, name)))
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (match.Name is null || match.Distance > Math.Max(2, entityOrRelationshipName.Length / 3))
            return ResolutionResult.NotFound(
                $"No close semantic match was found for '{entityOrRelationshipName}'.",
                [new ResolutionEvidence($"Fuzzy semantic search considered {names.Length} declared name(s).")]);

        return ResolutionResult.NotFound(
            $"Semantic name '{entityOrRelationshipName}' matched '{match.Name}', but fuzzy matching does not invent a record identity.",
            [
                new ResolutionEvidence(
                    $"Closest semantic match: '{match.Name}'. The caller must explicitly select the resulting semantic entity or relationship before data resolution.")
            ]);
    }

    /// <summary>
    /// Performs provider-backed approximate retrieval only when explicitly requested by the plan.
    /// Foundgine receives candidates and evidence; it never depends on Elasticsearch, vectors, or graph databases.
    /// </summary>
    public IReadOnlyList<RetrievalCandidate> Retrieve(SemanticRetrievalRequest request)
    {
        if (!_model.Entities.Any(x => x.Id == request.EntityType))
            throw new KeyNotFoundException($"Unknown semantic entity '{request.EntityType}'.");

        if (!SemanticRetrievalPlanner.RequiresApproximateRetrieval(request.Strategy))
            return [];

        if (_candidates is not IApproximateCandidateSource source)
            throw new NotSupportedException(
                $"The configured candidate source does not support {request.Strategy} retrieval.");

        var candidates = source.Retrieve(request);
        return candidates
            .Where(x => x.EntityType == request.EntityType)
            .Where(x => request.ReferenceIdentity is null || x.IdentityValue != request.ReferenceIdentity)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.RecordId, StringComparer.Ordinal)
            .Take(request.Limit)
            .ToArray();
    }

    private static ResolutionResult ResolveCandidates(
        EntityId entityType,
        string entityName,
        IReadOnlyList<IdentityCandidate> matches,
        IReadOnlyList<ResolutionEvidence> evidence,
        string kind)
    {
        if (matches.Count == 0)
            return ResolutionResult.NotFound($"No {entityName} matched the supplied {kind}.", evidence);
        if (matches.Count > 1)
            return ResolutionResult.Ambiguous($"{matches.Count} {entityName} records matched the supplied {kind}.",
                evidence);
        return ResolutionResult.Success(new ResolvedReference(entityType, matches[0].IdentityValue, 1.0,
            $"Resolved by {kind}.", evidence));
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1));
            previous = current;
        }

        return previous[b.Length];
    }
}