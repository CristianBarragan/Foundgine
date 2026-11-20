using System.Security.Claims;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Extensions.Keycloak.Configuration;
using Foundgine.Extensions.Keycloak.Warrants;

namespace Foundgine.Shop;

public static class ShopAuth
{
    /// <summary>Role -> entity read capabilities. Roots a role may START at are checked in GraphQLEndpoint.</summary>
    public static void MapGrants(FoundgineKeycloakOptions o)
    {
        foreach (var role in new[] { "manager", "customer" })
            o.RoleGrants[role] = new[] { "Customer", "Order", "OrderLine", "Product" }
                .Select(e => new FoundgineKeycloakGrant { Capability = $"{e}.read", Operation = "read" }).ToList();
    }
}

/// <summary>Plain JWT -> Foundgine: validated claims become a short-lived signed warrant. The token is the only input.</summary>
public sealed class JwtSecurityContextProvider(IHttpContextAccessor accessor, IFoundgineWarrantIssuer issuer)
    : ISecurityExecutionContextProvider
{
    public SecurityExecutionContext? GetSecurityExecutionContext()
    {
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return null;
        var sub = user.FindFirstValue("sub");
        var tenant = user.FindFirstValue("tenant_id");
        if (string.IsNullOrWhiteSpace(sub) || string.IsNullOrWhiteSpace(tenant)) return null;
        // A fresh warrant per call: the runtime consumes each warrant's nonce once.
        return issuer.Issue(sub, tenant, user.FindAll("role").Select(c => c.Value).ToArray());
    }
}

/// <summary>Row-level rule: a customer only sees their own Customer row and their own Orders.</summary>
public sealed class ShopAuthorizationPolicy(IHttpContextAccessor accessor) : ISemanticAuthorizationPolicy
{
    private static readonly EntityId Customer = EntityId.Create("Customer");
    private static readonly EntityId Order = EntityId.Create("Order");

    public bool CanAccessEntity(EntityId entityId) => true;
    public bool CanAccessField(EntityId entityId, FieldId fieldId) => true;
    public bool CanAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) => true;
    // Writes are denied by the interface defaults: this API is read-only.

    public AuthorizationPredicate? GetPredicate(EntityId entityId, AuthorizationOperation operation)
    {
        var user = accessor.HttpContext?.User;
        if (operation != AuthorizationOperation.Read || user?.IsInRole("customer") != true) return null;
        string member;
        if (entityId == Customer) member = "Id";
        else if (entityId == Order) member = "CustomerId";
        else return null;
        var row = AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), member);
        // Fail closed when the claim is missing.
        if (!int.TryParse(user.FindFirstValue("customer_id"), out _))
            return AuthorizationPredicate.Equal(row, AuthorizationPredicate.Constant("-1"));
        return AuthorizationPredicate.Equal(row, AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "CustomerId"));
    }
}
