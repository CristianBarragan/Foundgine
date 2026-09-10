package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import java.time.OffsetDateTime;
import java.util.*;
import java.util.stream.Collectors;

/**
 * Provider-independent semantic reference resolver.
 * Resolution never invents a data identity: zero candidates is NotFound and
 * multiple candidates is Ambiguous.
 */
public final class EntityResolver {
    private final SemanticModel model;
    private final ICandidateSource candidates;

    public EntityResolver(SemanticModel model, ICandidateSource candidates) {
        this.model = Objects.requireNonNull(model);
        this.candidates = Objects.requireNonNull(candidates);
    }

    public ResolutionResult resolveByIdentity(EntityId entityType, String identityLiteral) {
        var entity = model.get(entityType);
        var matches = candidates.findByIdentity(entityType, identityLiteral);
        var evidence = List.of(new ResolutionEvidence(
            "Looked up " + entity.name() + "." + entity.identity().name() + " = '" +
            identityLiteral + "': " + matches.size() + " match(es)."));
        if (matches.isEmpty()) return ResolutionResult.notFound(
            "No " + entity.name() + " found with " + entity.identity().name() + " '" + identityLiteral + "'.", evidence);
        if (matches.size() > 1) return ResolutionResult.ambiguous(
            matches.size() + " " + entity.name() + " records matched identity '" + identityLiteral + "'.", evidence);
        return ResolutionResult.success(new ResolvedReference(entityType, matches.get(0).identityValue(), 1.0,
            "Explicit identity matched " + entity.name() + "." + entity.identity().name() + ".", evidence));
    }

    public ResolutionResult resolveByRelationship(ResolvedReference source, String relationshipName) {
        var sourceEntity = model.get(source.entityType());
        var relationship = sourceEntity.relationships().stream()
            .filter(r -> r.name().equalsIgnoreCase(relationshipName)).findFirst().orElse(null);
        if (relationship == null) return ResolutionResult.notFound(
            sourceEntity.name() + " has no relationship named '" + relationshipName + "'.",
            List.of(new ResolutionEvidence("No relationship '" + relationshipName + "' declared on " + sourceEntity.name() + ".")));
        var matches = candidates.findByRelationship(relationship.id(), source.identityValue());
        var target = model.get(relationship.target());
        var evidence = List.of(new ResolutionEvidence(
            "Traversed " + sourceEntity.name() + "." + relationship.name() + ": " + matches.size() + " candidate(s)."));
        if (matches.isEmpty()) return ResolutionResult.notFound(
            "No " + target.name() + " found via " + sourceEntity.name() + "." + relationship.name() + ".", evidence);
        if (matches.size() > 1) return ResolutionResult.ambiguous(
            matches.size() + " " + target.name() + " found via " + sourceEntity.name() + "." + relationship.name() + ".", evidence);
        return ResolutionResult.success(new ResolvedReference(relationship.target(), matches.get(0).identityValue(), 0.9,
            "Uniquely resolved via " + sourceEntity.name() + "." + relationship.name() + ".", evidence));
    }

    public ResolutionResult resolveBySemanticIdentity(String entityName, String identityLiteral) {
        var entity = model.findEntity(entityName).orElse(null);
        if (entity == null) return ResolutionResult.notFound(
            "No semantic entity named '" + entityName + "' is defined.",
            List.of(new ResolutionEvidence("Semantic entity '" + entityName + "' was not found in the model.")));
        return resolveByIdentity(entity.id(), identityLiteral);
    }

    public ResolutionResult resolveByTraversal(ResolvedReference source, String traversalName) {
        var traversal = model.getTraversal(source.entityType(), traversalName);
        var current = source;
        var evidence = new ArrayList<ResolutionEvidence>();
        for (var relationshipId : traversal.path()) {
            var entity = model.get(current.entityType());
            var relationship = entity.relationships().stream().filter(x -> x.id().equals(relationshipId)).findFirst().orElse(null);
            if (relationship == null) return ResolutionResult.notFound(
                "Traversal '" + traversalName + "' contains an undeclared relationship '" + relationshipId + "'.", evidence);
            var result = resolveByRelationship(current, relationship.name());
            evidence.addAll(result.evidence());
            if (result.outcome() != ResolutionOutcome.RESOLVED) return result;
            current = result.resolved();
        }
        return ResolutionResult.success(new ResolvedReference(
            current.entityType(), current.identityValue(), current.confidence(), current.reason(), evidence));
    }

    public ResolutionResult resolveByCompositeKey(EntityId entityType, Map<String,String> identityValues) {
        if (!(candidates instanceof IAdvancedCandidateSource advanced))
            throw new UnsupportedOperationException("The configured candidate source does not support composite identity resolution.");
        var entity = model.get(entityType);
        var matches = advanced.findByCompositeIdentity(entityType, Map.copyOf(identityValues));
        var evidence = List.of(new ResolutionEvidence(
            "Looked up composite identity on " + entity.name() + ": " + matches.size() + " match(es)."));
        return resolveCandidates(entityType, entity.name(), matches, evidence, "composite identity");
    }

    public ResolutionResult resolveByTemporalIdentity(EntityId entityType, String identityLiteral, OffsetDateTime asOf) {
        if (!(candidates instanceof IAdvancedCandidateSource advanced))
            throw new UnsupportedOperationException("The configured candidate source does not support temporal identity resolution.");
        var entity = model.get(entityType);
        var matches = advanced.findByTemporalIdentity(entityType, identityLiteral, asOf);
        var evidence = List.of(new ResolutionEvidence(
            "Looked up " + entity.name() + "." + entity.identity().name() + " at " + asOf + ": " + matches.size() + " match(es)."));
        return resolveCandidates(entityType, entity.name(), matches, evidence, "temporal identity");
    }

    public ResolutionResult resolveByFuzzyMatch(String name, EntityId sourceEntity) {
        var names = model.get(sourceEntity).relationships().stream().map(SemanticRelationship::name).toList();
        return fuzzyResult(name, names);
    }
    public ResolutionResult resolveByFuzzyMatch(String name) {
        var names = model.entities().stream().map(SemanticEntity::name).toList();
        return fuzzyResult(name, names);
    }
    private ResolutionResult fuzzyResult(String name, List<String> names) {
        var match = names.stream().map(n -> new NameDistance(n, levenshtein(name,n)))
            .sorted(Comparator.comparingInt(NameDistance::distance).thenComparing(NameDistance::name, String.CASE_INSENSITIVE_ORDER))
            .findFirst().orElse(null);
        if (match == null || match.distance() > Math.max(2, name.length()/3))
            return ResolutionResult.notFound("No close semantic match was found for '" + name + "'.",
                List.of(new ResolutionEvidence("Fuzzy semantic search considered " + names.size() + " declared name(s).")));
        return ResolutionResult.notFound(
            "Semantic name '" + name + "' matched '" + match.name() + "', but fuzzy matching does not invent a record identity.",
            List.of(new ResolutionEvidence("Closest semantic match: '" + match.name() +
                "'. The caller must explicitly select the resulting semantic entity or relationship before data resolution.")));
    }

    public List<RetrievalCandidate> retrieve(SemanticRetrievalRequest request) {
        Objects.requireNonNull(request);
        model.get(request.entityType());
        if (!SemanticRetrievalPlanner.requiresApproximateRetrieval(request.strategy())) return List.of();
        if (!(candidates instanceof IApproximateCandidateSource source))
            throw new UnsupportedOperationException("The configured candidate source does not support " + request.strategy() + " retrieval.");
        return source.retrieve(request).stream()
            .filter(x -> x.entityType().equals(request.entityType()))
            .filter(x -> request.referenceIdentity() == null || !request.referenceIdentity().equals(x.identityValue()))
            .sorted(Comparator.comparingDouble(RetrievalCandidate::score).reversed().thenComparing(RetrievalCandidate::recordId))
            .limit(request.limit()).toList();
    }

    private static ResolutionResult resolveCandidates(EntityId type, String name, List<IdentityCandidate> matches,
                                                       List<ResolutionEvidence> evidence, String kind) {
        if (matches.isEmpty()) return ResolutionResult.notFound("No " + name + " matched the supplied " + kind + ".", evidence);
        if (matches.size() > 1) return ResolutionResult.ambiguous(matches.size() + " " + name + " records matched the supplied " + kind + ".", evidence);
        return ResolutionResult.success(new ResolvedReference(type, matches.get(0).identityValue(), 1.0, "Resolved by " + kind + ".", evidence));
    }
    private record NameDistance(String name, int distance) {}
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
}
