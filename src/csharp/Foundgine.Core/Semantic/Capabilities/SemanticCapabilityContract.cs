using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Security;

namespace Foundgine.Core.Semantic.Capabilities;

/// <summary>
/// Canonical, machine-readable description of the semantic application surface.
/// The contract is descriptive and never replaces execution-time authorization.
/// </summary>
public sealed record SemanticCapabilityContract(
    int Version,
    IReadOnlyList<SemanticCapability> Capabilities);

/// <summary>
/// A named capability exposed by the semantic model.
/// </summary>
public sealed record SemanticCapability(
    string Id,
    string Name,
    EntityId TargetEntityId,
    AuthorizationDecision Access,
    IReadOnlyList<SemanticCapabilityInput> Inputs,
    IReadOnlyList<SemanticCapabilityConstraint> Constraints,
    IReadOnlyList<SemanticCapabilityEffect> Effects,
    IReadOnlyList<string> Fields,
    IReadOnlyList<string> Relationships)
{
    /// <summary>Canonical semantic action represented by this capability.</summary>
    public string Operation { get; init; } = "read";

    /// <summary>Whether executing the action may mutate application state.</summary>
    public bool HasSideEffects { get; init; }

    /// <summary>Whether repeated execution is semantically safe without an idempotency key.</summary>
    public bool IsIdempotent { get; init; }

    /// <summary>Semantic compatibility version of this capability definition.</summary>
    public int Version { get; init; } = SemanticVersionSet.CurrentCapabilityVersion;

    /// <summary>Security guarantees that planners/providers must preserve.</summary>
    public IReadOnlyList<string> RequiredSecurityInvariants { get; init; } = [];

    /// <summary>Returns the canonical invariant set when callers did not explicitly supply one.</summary>
    public IReadOnlyList<string> EffectiveSecurityInvariants => RequiredSecurityInvariants.Count > 0
        ? RequiredSecurityInvariants
        : SemanticCapabilitySecurityDefaults.For(this);
}

/// <summary>
/// Describes one input accepted by a semantic capability.
/// </summary>
public sealed record SemanticCapabilityInput(
    string Name,
    string Type,
    bool Required,
    string? Description = null);

/// <summary>
/// Describes a semantic precondition or execution constraint.
/// </summary>
public sealed record SemanticCapabilityConstraint(
    string Name,
    string Description);

/// <summary>
/// Describes a side effect that may result from executing a capability.
/// </summary>
public sealed record SemanticCapabilityEffect(
    string Name,
    string Description);

/// <summary>
/// Builds the first canonical capability contract from the existing semantic
/// model and authorization capability surface. Providers and transports should
/// consume this contract rather than constructing their own semantic schemas.
/// </summary>
public static class SemanticCapabilityContractDiscovery
{
    public const int CurrentVersion = 1;

    public static SemanticCapabilityContract Describe(
        SemanticModel model,
        ISemanticAuthorizationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(policy);

        var discovered = SemanticAuthorizationCapabilityDiscovery.Describe(model, policy);
        // Capability discovery intentionally hides authorization predicates, but
        // the canonical semantic contract must retain the exact policy predicate
        // for planning/security consumers that need to carry it forward.
        // Rehydrate only the top-level entity decision here; the descriptive
        // authorization capability surface remains predicate-free.
        var capabilities = discovered.Entities
            .Select(entity => entity with
            {
                Read = PreservePredicate(entity.Read,
                    policy.GetPredicate(entity.EntityId, AuthorizationOperation.Read)),
                Write = PreservePredicate(entity.Write,
                    policy.GetPredicate(entity.EntityId, AuthorizationOperation.Write))
            })
            .SelectMany(entity => BuildCapabilities(model, entity, policy))
            .OrderBy(capability => capability.Id, StringComparer.Ordinal)
            .ToArray();

        return new SemanticCapabilityContract(CurrentVersion, capabilities);
    }


    private static AuthorizationDecision PreservePredicate(
        AuthorizationDecision decision,
        AuthorizationPredicate? predicate) =>
        predicate is not null
            ? AuthorizationDecision.Conditional(predicate)
            : decision;

    private static IEnumerable<SemanticCapability> BuildCapabilities(
        SemanticModel model,
        SemanticAuthorizationCapability entity,
        ISemanticAuthorizationPolicy policy)
    {
        yield return new SemanticCapability(
            Id: $"{entity.Name}.read",
            Name: $"Read {entity.Name}",
            TargetEntityId: entity.EntityId,
            Access: entity.Read,
            Inputs: [],
            Constraints: [],
            Effects: [],
            Fields: entity.Fields
                .Where(x => x.Read.IsAllowed)
                .Select(x => x.Name)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Relationships: entity.Relationships
                .Where(x => x.Read.IsAllowed)
                .Select(x => x.Name)
                .Concat(model.Traversals
                    .Where(x => x.Source == entity.EntityId && TraversalIsReadable(model, x, policy))
                    .Select(x => x.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray())
        {
            Operation = "read",
            HasSideEffects = false,
            IsIdempotent = true
        };

        yield return new SemanticCapability(
            Id: $"{entity.Name}.write",
            Name: $"Write {entity.Name}",
            TargetEntityId: entity.EntityId,
            Access: entity.Write,
            Inputs: BuildWriteInputs(model, entity),
            Constraints: BuildWriteConstraints(),
            Effects: entity.Write.IsAllowed
                ?
                [
                    new SemanticCapabilityEffect(
                        "data.write",
                        $"May modify {entity.Name} data when execution-time authorization permits it.")
                ]
                : [],
            Fields: entity.Fields
                .Where(x => x.Write.IsAllowed)
                .Select(x => x.Name)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Relationships: entity.Relationships
                .Where(x => x.Write.IsAllowed)
                .Select(x => x.Name)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray())
        {
            Operation = "write",
            HasSideEffects = entity.Write.IsAllowed,
            IsIdempotent = false
        };

        if (entity.Write.IsAllowed)
        {
            foreach (var action in BuildMutationActions(model, entity))
                yield return action;
        }

        foreach (var relationship in entity.Relationships.Where(x => x.Read.IsAllowed))
        {
            yield return new SemanticCapability(
                Id: $"{entity.Name}.{relationship.Name}.traverse",
                Name: $"Traverse {entity.Name}.{relationship.Name}",
                TargetEntityId: relationship.TargetEntityId,
                Access: relationship.Read,
                Inputs: [],
                Constraints: [],
                Effects: [],
                Fields: [],
                Relationships: [])
            {
                Operation = "traverse",
                HasSideEffects = false,
                IsIdempotent = true
            };
        }

        foreach (var traversal in model.Traversals.Where(x =>
                     x.Source == entity.EntityId && TraversalIsReadable(model, x, policy)))
        {
            yield return new SemanticCapability(
                Id: $"{entity.Name}.{traversal.Name}.traverse",
                Name: $"Traverse {entity.Name}.{traversal.Name}",
                TargetEntityId: traversal.Target,
                Access: AuthorizationDecision.Allowed,
                Inputs: [],
                Constraints:
                [
                    new SemanticCapabilityConstraint(
                        "semantic-path",
                        $"Logical traversal expands through relationship path {string.Join(" -> ", traversal.Path.Select(x => x.Value))}; every hop remains subject to execution-time authorization.")
                ],
                Effects: [],
                Fields: [],
                Relationships: [])
            {
                Operation = "traverse",
                HasSideEffects = false,
                IsIdempotent = true
            };
        }
    }


    private static bool TraversalIsReadable(
        SemanticModel model,
        SemanticTraversal traversal,
        ISemanticAuthorizationPolicy policy)
    {
        var current = model.Get(traversal.Source);
        foreach (var relationshipId in traversal.Path)
        {
            var relationship = current.Relationships.FirstOrDefault(x => x.Id == relationshipId);
            if (relationship is null || !policy
                    .GetRelationshipAccess(current.Id, relationship.Id, AuthorizationOperation.Read).IsAllowed)
                return false;

            current = model.Get(relationship.Target);
            if (!policy.GetEntityAccess(current.Id, AuthorizationOperation.Read).IsAllowed)
                return false;
        }

        return true;
    }

    private static IReadOnlyList<SemanticCapabilityConstraint> BuildWriteConstraints() =>
    [
        new("authorization", "Execution-time authorization must permit the requested mutation."),
        new("writable-fields", "Every requested field must be writable under the effective authorization policy.")
    ];

    private static IEnumerable<SemanticCapability> BuildMutationActions(
        SemanticModel model,
        SemanticAuthorizationCapability entity)
    {
        foreach (var action in new[] { "create", "update", "delete", "upsert" })
        {
            var constraints = action switch
            {
                "create" => new[]
                {
                    new SemanticCapabilityConstraint("writable-fields", "Every supplied field must be writable.")
                },
                "update" => new[]
                {
                    new SemanticCapabilityConstraint("target-selection",
                        "A target filter or equivalent identity selection is required."),
                    new SemanticCapabilityConstraint("writable-fields", "Every supplied field must be writable.")
                },
                "delete" => new[]
                {
                    new SemanticCapabilityConstraint("target-selection",
                        "A target filter or equivalent identity selection is required.")
                },
                "upsert" => new[]
                {
                    new SemanticCapabilityConstraint("conflict-key",
                        "A conflict key or equivalent identity must determine the upsert target."),
                    new SemanticCapabilityConstraint("writable-fields", "Every supplied field must be writable.")
                },
                _ => Array.Empty<SemanticCapabilityConstraint>()
            };

            var effects = new List<SemanticCapabilityEffect>
            {
                new($"data.{action}", $"May {action} {entity.Name} data when execution-time authorization permits it.")
            };

            if (action is "create" or "update" or "upsert")
                effects.Add(new SemanticCapabilityEffect("field.mutation", "May change writable field values."));

            yield return new SemanticCapability(
                Id: $"{entity.Name}.{action}",
                Name:
                $"{action switch { "create" => "Create", "update" => "Update", "delete" => "Delete", "upsert" => "Upsert", _ => action }} {entity.Name}",
                TargetEntityId: entity.EntityId,
                Access: entity.Write,
                Inputs: action == "delete" ? [] : BuildWriteInputs(model, entity),
                Constraints: constraints,
                Effects: effects,
                Fields: entity.Fields.Where(x => x.Write.IsAllowed).Select(x => x.Name)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                Relationships: entity.Relationships.Where(x => x.Write.IsAllowed).Select(x => x.Name)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray())
            {
                Operation = action,
                HasSideEffects = entity.Write.IsAllowed,
                IsIdempotent = action is "update" or "delete" or "upsert"
            };
        }
    }

    private static IReadOnlyList<SemanticCapabilityInput> BuildWriteInputs(
        SemanticModel model,
        SemanticAuthorizationCapability entity)
    {
        var writableFields = entity.Fields
            .Where(x => x.Write.IsAllowed)
            .Select(field => new SemanticCapabilityInput(
                field.Name,
                field.ClrTypeName(model, entity.EntityId),
                Required: false,
                Description: $"Writable field on {entity.Name}."))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return writableFields;
    }

    private static string ClrTypeName(
        this SemanticFieldAuthorizationCapability field,
        SemanticModel model,
        EntityId entityId)
    {
        var semanticField = model.Get(entityId).Fields.Single(x => x.Id == field.FieldId);
        return semanticField.ClrType.FullName ?? semanticField.ClrType.Name;
    }
}