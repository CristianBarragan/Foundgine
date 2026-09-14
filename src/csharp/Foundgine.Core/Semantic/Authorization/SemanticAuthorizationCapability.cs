using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Authorization;

/// <summary>Read/write capability information for one semantic field.</summary>
public sealed record SemanticFieldAuthorizationCapability(
    FieldId FieldId,
    string Name,
    AuthorizationDecision Read,
    AuthorizationDecision Write);

/// <summary>Read/write capability information for one semantic relationship.</summary>
public sealed record SemanticRelationshipAuthorizationCapability(
    RelationshipId RelationshipId,
    string Name,
    EntityId TargetEntityId,
    AuthorizationDecision Read,
    AuthorizationDecision Write);

/// <summary>
/// Capability information for one entity. This is descriptive context for
/// callers such as APIs and AI agents; it is not an authorization bypass.
/// Execution still evaluates the policy independently.
/// </summary>
public sealed record SemanticAuthorizationCapability(
    EntityId EntityId,
    string Name,
    AuthorizationDecision Read,
    AuthorizationDecision Write,
    IReadOnlyList<SemanticFieldAuthorizationCapability> Fields,
    IReadOnlyList<SemanticRelationshipAuthorizationCapability> Relationships);

/// <summary>Complete semantic capability description for the current policy.</summary>
public sealed record SemanticAuthorizationCapabilities(
    IReadOnlyList<SemanticAuthorizationCapability> Entities);

/// <summary>Builds a safe, provider-independent capability description.</summary>
public static class SemanticAuthorizationCapabilityDiscovery
{
    public static SemanticAuthorizationCapabilities Describe(
        SemanticModel model,
        ISemanticAuthorizationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(policy);

        var entities = model.Entities
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entity => DescribeEntity(model, policy, entity))
            .ToArray();

        return new SemanticAuthorizationCapabilities(entities);
    }

    private static SemanticAuthorizationCapability DescribeEntity(
        SemanticModel model,
        ISemanticAuthorizationPolicy policy,
        SemanticEntity entity)
    {
        var read = Effective(
            policy.GetEntityAccess(entity.Id, AuthorizationOperation.Read),
            PredicateDecision(policy.GetPredicate(entity.Id, AuthorizationOperation.Read)));
        var write = Effective(
            policy.GetEntityAccess(entity.Id, AuthorizationOperation.Write),
            PredicateDecision(policy.GetPredicate(entity.Id, AuthorizationOperation.Write)));

        var fields = entity.Fields
            .OrderBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .Select(field => new SemanticFieldAuthorizationCapability(
                field.Id,
                field.Name,
                DescribeDecision(Effective(read,
                    policy.GetFieldAccess(entity.Id, field.Id, AuthorizationOperation.Read))),
                DescribeDecision(Effective(write,
                    policy.GetFieldAccess(entity.Id, field.Id, AuthorizationOperation.Write)))))
            .ToArray();

        var relationships = entity.Relationships
            .OrderBy(relationship => relationship.Name, StringComparer.OrdinalIgnoreCase)
            .Select(relationship =>
            {
                var target = model.Get(relationship.Target);
                var targetRead = policy.GetEntityAccess(target.Id, AuthorizationOperation.Read);
                var targetWrite = policy.GetEntityAccess(target.Id, AuthorizationOperation.Write);

                return new SemanticRelationshipAuthorizationCapability(
                    relationship.Id,
                    relationship.Name,
                    relationship.Target,
                    DescribeDecision(Effective(read, targetRead, policy.GetRelationshipAccess(
                        entity.Id, relationship.Id, AuthorizationOperation.Read))),
                    DescribeDecision(Effective(write, targetWrite, policy.GetRelationshipAccess(
                        entity.Id, relationship.Id, AuthorizationOperation.Write))));
            })
            .ToArray();

        return new SemanticAuthorizationCapability(
            entity.Id,
            entity.Name,
            DescribeDecision(read),
            DescribeDecision(write),
            fields,
            relationships);
    }

    private static AuthorizationDecision DescribeDecision(AuthorizationDecision decision) =>
        new(decision.Access);

    private static AuthorizationDecision PredicateDecision(AuthorizationPredicate? predicate) =>
        predicate is null
            ? AuthorizationDecision.Allowed
            : AuthorizationDecision.Conditional(predicate);

    private static AuthorizationDecision Effective(params AuthorizationDecision[] decisions) =>
        SemanticAuthorizationCapabilityComposition.Compose(decisions);
}