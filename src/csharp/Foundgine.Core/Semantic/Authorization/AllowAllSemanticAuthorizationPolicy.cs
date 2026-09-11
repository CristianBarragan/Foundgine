using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Authorization;

/// <summary>Default unrestricted policy for applications that do not need authorization.</summary>
public class AllowAllSemanticAuthorizationPolicy : ISemanticAuthorizationPolicy
{
    public virtual bool CanAccessEntity(EntityId entityId) => true;
    public virtual bool CanAccessField(EntityId entityId, FieldId fieldId) => true;
    public virtual bool CanAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) => true;
    public virtual bool CanWriteEntity(EntityId entityId) => true;
    public virtual bool CanWriteField(EntityId entityId, FieldId fieldId) => true;
    public virtual bool CanWriteRelationship(EntityId sourceEntityId, RelationshipId relationshipId) => true;

    public virtual AuthorizationPredicate? GetPredicate(
        EntityId entityId,
        AuthorizationOperation operation) => null;

    public virtual AuthorizationDecision GetEntityAccess(
        EntityId entityId,
        AuthorizationOperation operation) =>
        operation == AuthorizationOperation.Read
            ? (CanAccessEntity(entityId) ? AuthorizationDecision.Allowed : AuthorizationDecision.Denied)
            : (CanWriteEntity(entityId) ? AuthorizationDecision.Allowed : AuthorizationDecision.Denied);

    /// <summary>
    /// Named-operation refinement of the coarse Read/Write decision above.
    /// Declared as a real virtual class member (not just an interface default
    /// method) so derived policies can use <c>override</c> to layer
    /// additional, domain-specific requirements onto specific operation
    /// names. The default simply falls back to the coarse decision, matching
    /// <see cref="ISemanticAuthorizationPolicy"/>'s own default.
    /// </summary>
    public virtual AuthorizationDecision GetEntityAccess(
        EntityId entityId,
        AuthorizationOperation operation,
        AuthorizationOperationName? name) =>
        GetEntityAccess(entityId, operation);

    public virtual AuthorizationDecision GetFieldAccess(
        EntityId entityId,
        FieldId fieldId,
        AuthorizationOperation operation) =>
        operation == AuthorizationOperation.Read
            ? (CanAccessField(entityId, fieldId) ? AuthorizationDecision.Allowed : AuthorizationDecision.Denied)
            : (CanWriteField(entityId, fieldId) ? AuthorizationDecision.Allowed : AuthorizationDecision.Denied);

    public virtual AuthorizationDecision GetRelationshipAccess(
        EntityId sourceEntityId,
        RelationshipId relationshipId,
        AuthorizationOperation operation) =>
        operation == AuthorizationOperation.Read
            ? (CanAccessRelationship(sourceEntityId, relationshipId)
                ? AuthorizationDecision.Allowed
                : AuthorizationDecision.Denied)
            : (CanWriteRelationship(sourceEntityId, relationshipId)
                ? AuthorizationDecision.Allowed
                : AuthorizationDecision.Denied);
}