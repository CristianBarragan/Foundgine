using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Authorization;

/// <summary>Application-owned actor context consumed by configured semantic authorization rules.</summary>
public sealed record SemanticAuthorizationContext(
    string? TenantId = null,
    string? Role = null,
    IReadOnlyDictionary<string, string>? Claims = null)
{
    public IReadOnlyDictionary<string, string> SafeClaims => Claims ?? EmptyClaims.Instance;

    private sealed class EmptyClaims : Dictionary<string, string>
    {
        public static readonly EmptyClaims Instance = new();
    }
}

/// <summary>Reusable configuration for provider-independent semantic authorization.</summary>
public sealed class SemanticAuthorizationConfiguration
{
    private readonly List<Func<SemanticAuthorizationContext, EntityId, AuthorizationOperation, bool>> _entities = [];

    private readonly List<Func<SemanticAuthorizationContext, EntityId, FieldId, AuthorizationOperation, bool>> _fields =
        [];

    private readonly List<Func<SemanticAuthorizationContext, EntityId, RelationshipId, AuthorizationOperation, bool>>
        _relationships = [];

    private readonly List<Func<SemanticAuthorizationContext, EntityId, AuthorizationOperation, AuthorizationPredicate?>>
        _predicates = [];

    private readonly
        List<Func<SemanticAuthorizationContext, EntityId, AuthorizationOperation, AuthorizationOperationName?,
            AuthorizationDecision?>> _operations = [];

    public SemanticAuthorizationConfiguration AllowAll() =>
        AddEntityRule((_, _, _) => true)
            .AddFieldRule((_, _, _, _) => true)
            .AddRelationshipRule((_, _, _, _) => true);

    public SemanticAuthorizationConfiguration AddEntityRule(
        Func<SemanticAuthorizationContext, EntityId, AuthorizationOperation, bool> rule)
    {
        _entities.Add(rule ?? throw new ArgumentNullException(nameof(rule)));
        return this;
    }

    public SemanticAuthorizationConfiguration AddFieldRule(
        Func<SemanticAuthorizationContext, EntityId, FieldId, AuthorizationOperation, bool> rule)
    {
        _fields.Add(rule ?? throw new ArgumentNullException(nameof(rule)));
        return this;
    }

    public SemanticAuthorizationConfiguration AddRelationshipRule(
        Func<SemanticAuthorizationContext, EntityId, RelationshipId, AuthorizationOperation, bool> rule)
    {
        _relationships.Add(rule ?? throw new ArgumentNullException(nameof(rule)));
        return this;
    }

    public SemanticAuthorizationConfiguration AddPredicateRule(
        Func<SemanticAuthorizationContext, EntityId, AuthorizationOperation, AuthorizationPredicate?> rule)
    {
        _predicates.Add(rule ?? throw new ArgumentNullException(nameof(rule)));
        return this;
    }

    public SemanticAuthorizationConfiguration AddOperationRule(
        Func<SemanticAuthorizationContext, EntityId, AuthorizationOperation, AuthorizationOperationName?,
            AuthorizationDecision?> rule)
    {
        _operations.Add(rule ?? throw new ArgumentNullException(nameof(rule)));
        return this;
    }

    internal bool CanAccessEntity(SemanticAuthorizationContext context, EntityId id,
        AuthorizationOperation operation) =>
        _entities.Count == 0 ? false : _entities.All(rule => rule(context, id, operation));

    internal bool CanAccessField(SemanticAuthorizationContext context, EntityId entity, FieldId field,
        AuthorizationOperation operation) =>
        _fields.Count == 0 ? false : _fields.All(rule => rule(context, entity, field, operation));

    internal bool CanAccessRelationship(SemanticAuthorizationContext context, EntityId entity,
        RelationshipId relationship, AuthorizationOperation operation) =>
        _relationships.Count == 0 ? false : _relationships.All(rule => rule(context, entity, relationship, operation));

    internal AuthorizationPredicate? GetPredicate(SemanticAuthorizationContext context, EntityId entity,
        AuthorizationOperation operation)
    {
        AuthorizationPredicate? result = null;
        foreach (var rule in _predicates)
        {
            var predicate = rule(context, entity, operation);
            if (predicate is not null)
                result = result is null ? predicate : AuthorizationPredicate.And(result, predicate);
        }

        return result;
    }

    internal AuthorizationDecision? GetOperationDecision(SemanticAuthorizationContext context, EntityId entity,
        AuthorizationOperation operation, AuthorizationOperationName? name)
    {
        AuthorizationDecision? result = null;
        foreach (var rule in _operations)
        {
            var decision = rule(context, entity, operation, name);
            if (decision is not null)
                result = result is null ? decision : AuthorizationDecision.Combine(result, decision);
        }

        return result;
    }
}

/// <summary>Authorization policy backed entirely by application configuration.</summary>
public sealed class ConfiguredSemanticAuthorizationPolicy : ISemanticAuthorizationPolicy
{
    private readonly SemanticAuthorizationConfiguration _configuration;
    private readonly SemanticAuthorizationContext _context;

    public ConfiguredSemanticAuthorizationPolicy(
        SemanticAuthorizationConfiguration configuration,
        SemanticAuthorizationContext context)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public bool CanAccessEntity(EntityId entityId) =>
        _configuration.CanAccessEntity(_context, entityId, AuthorizationOperation.Read);

    public bool CanAccessField(EntityId entityId, FieldId fieldId) =>
        _configuration.CanAccessField(_context, entityId, fieldId, AuthorizationOperation.Read);

    public bool CanAccessRelationship(EntityId entityId, RelationshipId relationshipId) =>
        _configuration.CanAccessRelationship(_context, entityId, relationshipId, AuthorizationOperation.Read);

    public bool CanWriteEntity(EntityId entityId) =>
        _configuration.CanAccessEntity(_context, entityId, AuthorizationOperation.Write);

    public bool CanWriteField(EntityId entityId, FieldId fieldId) =>
        _configuration.CanAccessField(_context, entityId, fieldId, AuthorizationOperation.Write);

    public bool CanWriteRelationship(EntityId entityId, RelationshipId relationshipId) =>
        _configuration.CanAccessRelationship(_context, entityId, relationshipId, AuthorizationOperation.Write);

    public AuthorizationPredicate? GetPredicate(EntityId entityId, AuthorizationOperation operation) =>
        _configuration.GetPredicate(_context, entityId, operation);

    public AuthorizationDecision GetEntityAccess(EntityId entityId, AuthorizationOperation operation) =>
        operation == AuthorizationOperation.Read
            ? (CanAccessEntity(entityId) ? AuthorizationDecision.Allowed : AuthorizationDecision.Denied)
            : (CanWriteEntity(entityId) ? AuthorizationDecision.Allowed : AuthorizationDecision.Denied);

    public AuthorizationDecision GetEntityAccess(EntityId entityId, AuthorizationOperation operation,
        AuthorizationOperationName? name)
    {
        var coarse = GetEntityAccess(entityId, operation);
        if (!coarse.IsAllowed) return coarse;
        return AuthorizationDecision.Combine(coarse,
            _configuration.GetOperationDecision(_context, entityId, operation, name) ?? AuthorizationDecision.Allowed);
    }

    public AuthorizationDecision GetFieldAccess(EntityId entityId, FieldId fieldId, AuthorizationOperation operation) =>
        operation == AuthorizationOperation.Read
            ? (CanAccessField(entityId, fieldId) ? AuthorizationDecision.Allowed : AuthorizationDecision.Denied)
            : (CanWriteField(entityId, fieldId) ? AuthorizationDecision.Allowed : AuthorizationDecision.Denied);

    public AuthorizationDecision GetRelationshipAccess(EntityId sourceEntityId, RelationshipId relationshipId,
        AuthorizationOperation operation) =>
        operation == AuthorizationOperation.Read
            ? (CanAccessRelationship(sourceEntityId, relationshipId)
                ? AuthorizationDecision.Allowed
                : AuthorizationDecision.Denied)
            : (CanWriteRelationship(sourceEntityId, relationshipId)
                ? AuthorizationDecision.Allowed
                : AuthorizationDecision.Denied);
}