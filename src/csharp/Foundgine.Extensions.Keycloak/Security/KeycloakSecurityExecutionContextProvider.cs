using System.Security.Claims;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Extensions.Keycloak.Claims;
using Foundgine.Extensions.Keycloak.Configuration;
using Foundgine.Extensions.Keycloak.Warrants;
using Microsoft.AspNetCore.Http;

namespace Foundgine.Extensions.Keycloak.Security;

/// <summary>
/// Host-side <see cref="ISecurityExecutionContextProvider"/>: the validated Keycloak access token is the only input.
/// Subject, tenant and roles are read from the authenticated principal, never from the request body or headers.
/// Returns null (so callers fail as unauthenticated) when the token has no subject, no tenant, or no mapped grants.
/// </summary>
public sealed class KeycloakSecurityExecutionContextProvider(
    IHttpContextAccessor accessor, IFoundgineWarrantIssuer issuer, FoundgineKeycloakOptions options)
    : ISecurityExecutionContextProvider
{
    public SecurityExecutionContext? GetSecurityExecutionContext()
    {
        var http = accessor.HttpContext;
        if (http?.User.Identity?.IsAuthenticated != true) return null;

        // A fresh warrant on every call. The runtime consumes each warrant's nonce once (replay protection), so handing
        // the same warrant to a second execution in one request would be rejected as a replay. Authority stays
        // consistent because every warrant is derived from the same validated token.
        var user = http.User;
        var subject = user.FindFirstValue("sub");
        var tenant = user.FindFirstValue(options.TenantClaim);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(tenant)) return null;
        return issuer.Issue(subject, tenant, KeycloakClaims.ReadRoles(user, options));
    }
}
