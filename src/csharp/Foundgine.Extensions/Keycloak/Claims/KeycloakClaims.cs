using System.Security.Claims;
using System.Text.Json;
using Foundgine.Extensions.Keycloak.Configuration;

namespace Foundgine.Extensions.Keycloak.Claims;

/// <summary>Reads Keycloak's nested role claims (<c>realm_access</c>, <c>resource_access</c>).</summary>
public static class KeycloakClaims
{
    public static IReadOnlyCollection<string> ReadRoles(ClaimsPrincipal user, FoundgineKeycloakOptions options)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal);
        if (options.IncludeRealmRoles)
            foreach (var c in user.FindAll("realm_access")) AddRoles(c.Value, null, roles);
        if (options.IncludeClientRoles)
            foreach (var c in user.FindAll("resource_access")) AddRoles(c.Value, options.Audience, roles);
        return roles;
    }

    private static void AddRoles(string json, string? client, HashSet<string> roles)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (client is not null)
            {
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(client, out root)) return;
            }
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("roles", out var array) &&
                array.ValueKind == JsonValueKind.Array)
                foreach (var r in array.EnumerateArray())
                    if (r.ValueKind == JsonValueKind.String && r.GetString() is { Length: > 0 } role)
                        roles.Add(role);
        }
        catch (JsonException)
        {
            // A malformed claim yields no roles (deny by default).
        }
    }

    /// <summary>Projects Keycloak roles to <see cref="ClaimTypes.Role"/> so ASP.NET Core role policies work.</summary>
    internal static void AddRoleClaims(ClaimsPrincipal principal, FoundgineKeycloakOptions options)
    {
        if (principal.Identity is not ClaimsIdentity identity) return;
        foreach (var role in ReadRoles(principal, options))
            if (!identity.HasClaim(ClaimTypes.Role, role)) identity.AddClaim(new Claim(ClaimTypes.Role, role));
    }
}
