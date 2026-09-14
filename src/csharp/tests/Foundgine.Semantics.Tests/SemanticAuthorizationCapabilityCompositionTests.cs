using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticAuthorizationCapabilityCompositionTests
{
    [Fact]
    public void Compose_with_no_decisions_is_allowed()
    {
        var result = SemanticAuthorizationCapabilityComposition.Compose();

        Assert.Equal(AuthorizationAccess.Allowed, result.Access);
        Assert.Null(result.Predicate);
    }

    [Fact]
    public void Compose_intersects_rather_than_unions()
    {
        var result = SemanticAuthorizationCapabilityComposition.Compose(
            AuthorizationDecision.Allowed,
            AuthorizationDecision.Denied,
            AuthorizationDecision.Allowed);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Compose_ands_predicates_across_every_input()
    {
        var tenantPredicate = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(
                AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(
                AuthorizationPredicate.ContextParameter("user"), "TenantId"));

        var ownerPredicate = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(
                AuthorizationPredicate.ResourceParameter("resource"), "OwnerId"),
            AuthorizationPredicate.Member(
                AuthorizationPredicate.ContextParameter("user"), "Id"));

        var result = SemanticAuthorizationCapabilityComposition.Compose(
            AuthorizationDecision.Conditional(tenantPredicate),
            AuthorizationDecision.Conditional(ownerPredicate));

        Assert.Equal(AuthorizationAccess.Conditional, result.Access);
        Assert.Equal(
            AuthorizationPredicate.And(tenantPredicate, ownerPredicate),
            result.Predicate);
    }

    [Fact]
    public void Compose_never_widens_a_denial_regardless_of_input_order()
    {
        var denyFirst = SemanticAuthorizationCapabilityComposition.Compose(
            AuthorizationDecision.Denied, AuthorizationDecision.Allowed);
        var denyLast = SemanticAuthorizationCapabilityComposition.Compose(
            AuthorizationDecision.Allowed, AuthorizationDecision.Denied);

        Assert.False(denyFirst.IsAllowed);
        Assert.False(denyLast.IsAllowed);
    }
}

public sealed class AuthorizationOperationNamePolicyTests
{
    private sealed class CoarseOnlyPolicy : ISemanticAuthorizationPolicy
    {
        public bool CanAccessEntity(EntityId entityId) => true;
        public bool CanAccessField(EntityId entityId, FieldId fieldId) => true;
        public bool CanAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) => true;
        public bool CanWriteEntity(EntityId entityId) => true;
    }

    private sealed class NamedOperationPolicy : ISemanticAuthorizationPolicy
    {
        public bool CanAccessEntity(EntityId entityId) => true;
        public bool CanAccessField(EntityId entityId, FieldId fieldId) => true;
        public bool CanAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) => true;
        public bool CanWriteEntity(EntityId entityId) => true;

        public AuthorizationDecision GetEntityAccess(
            EntityId entityId,
            AuthorizationOperation operation,
            AuthorizationOperationName? name) =>
            name is { Value: "Invoice.Pay" }
                ? AuthorizationDecision.Denied
                : AuthorizationDecision.Allowed;
    }

    [Fact]
    public void Default_named_overload_falls_back_to_coarse_decision()
    {
        ISemanticAuthorizationPolicy policy = new CoarseOnlyPolicy();

        var decision = policy.GetEntityAccess(
            new EntityId(1), AuthorizationOperation.Write, new AuthorizationOperationName("Invoice.Pay"));

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Overridden_named_operation_can_narrow_beyond_coarse_write_gate()
    {
        ISemanticAuthorizationPolicy policy = new NamedOperationPolicy();

        var pay = policy.GetEntityAccess(
            new EntityId(1), AuthorizationOperation.Write, new AuthorizationOperationName("Invoice.Pay"));
        var update = policy.GetEntityAccess(
            new EntityId(1), AuthorizationOperation.Write, new AuthorizationOperationName("Invoice.Update"));

        Assert.False(pay.IsAllowed);
        Assert.True(update.IsAllowed);
    }
}
