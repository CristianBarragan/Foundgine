using System.Security.Claims;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.SupplyChain.Domain;
using Microsoft.AspNetCore.Http;

namespace Foundgine.SupplyChain.BenchmarkApi;

/// <summary>
/// The signed Keycloak warrant is checked by the Foundgine runtime for every requested capability.
/// This policy adds the row-level customer ownership predicate for customer reads; it never derives
/// authority from GraphQL arguments. The SQL provider receives the customer id as a bound execution
/// context value, not as query text.
/// </summary>
public sealed class WarrantSemanticAuthorizationPolicy(IHttpContextAccessor httpContextAccessor)
    : ISemanticAuthorizationPolicy
{
    private const string CustomerRole = "sc-customer";

    public bool CanAccessEntity(EntityId entityId) => true;
    public bool CanAccessField(EntityId entityId, FieldId fieldId) => true;
    public bool CanAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) => true;
    public bool CanWriteEntity(EntityId entityId) => true;
    public bool CanWriteField(EntityId entityId, FieldId fieldId) => true;
    public bool CanWriteRelationship(EntityId sourceEntityId, RelationshipId relationshipId) => true;

    public AuthorizationPredicate? GetPredicate(EntityId entityId, AuthorizationOperation operation)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (operation != AuthorizationOperation.Read ||
            entityId != SupplyChainSemantics.SalesOrder.Entity ||
            principal?.Identity?.IsAuthenticated != true ||
            !principal.IsInRole(CustomerRole))
            return null;

        // The HTTP boundary rejects a customer token without a numeric customer_id.
        // Keeping the policy fail-closed also protects non-HTTP callers that reuse this policy.
        if (!int.TryParse(principal.FindFirstValue("customer_id"), out _))
            return AuthorizationPredicate.Equal(
                AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "CustomerId"),
                AuthorizationPredicate.Constant("-1"));

        return AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "CustomerId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "CustomerId"));
    }
}
