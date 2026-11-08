using Foundgine.Core.Semantic.Security.Warrants;

namespace Foundgine.Extensions.Keycloak.Configuration;

/// <summary>One Foundgine capability grant given to holders of a Keycloak role. Config-bindable.</summary>
public sealed class FoundgineKeycloakGrant
{
    public string Capability { get; set; } = "";
    public string Operation { get; set; } = "";
    public List<string> ResourceScopes { get; set; } = [];
}

/// <summary>Keycloak identity integration. Bind from configuration (section "Foundgine:Keycloak") or set in code.</summary>
public sealed class FoundgineKeycloakOptions
{
    // ---- Token validation (resource-server side) ----

    /// <summary>Realm issuer URL, e.g. <c>https://id.example.com/realms/foundgine</c>.</summary>
    public string Authority { get; set; } = "";
    public string? MetadataAddress { get; set; }
    /// <summary>The <c>aud</c> a token must carry (add an audience mapper to the Keycloak client).</summary>
    public string Audience { get; set; } = "foundgine-api";
    public bool RequireHttpsMetadata { get; set; } = true;
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Claim holding the tenant (add a user-attribute mapper in Keycloak). Tokens without it are rejected.</summary>
    public string TenantClaim { get; set; } = "tenant_id";
    /// <summary>User/group attribute that stores the tenant (the attribute the Keycloak mapper reads into <see cref="TenantClaim"/>).</summary>
    public string TenantAttribute { get; set; } = "tenant_id";
    public bool IncludeRealmRoles { get; set; } = true;
    /// <summary>Also read <c>resource_access[Audience].roles</c>.</summary>
    public bool IncludeClientRoles { get; set; } = true;

    // ---- Warrant issuance (Keycloak identity -> Foundgine authority) ----

    public string WarrantIssuer { get; set; } = "foundgine-keycloak-bridge";
    /// <summary>Audience of issued warrants; must match what the Foundgine runtime verifies.</summary>
    public string WarrantAudience { get; set; } = "foundgine";
    public string WarrantKeyId { get; set; } = "foundgine-warrant-1";
    public TimeSpan WarrantLifetime { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>PEM file holding the RSA private key (2048+ bits). Prefer a mounted secret.</summary>
    public string? WarrantPrivateKeyPemPath { get; set; }
    public string? WarrantPrivateKeyPem { get; set; }

    /// <summary>Keycloak role -> Foundgine grants. A role not listed grants nothing; no grants means no context.</summary>
    public Dictionary<string, List<FoundgineKeycloakGrant>> RoleGrants { get; } = new(StringComparer.Ordinal);

    /// <summary>Optional tighter constraints per tenant/roles. Default: allowed tenants = [tenant], nothing else set.</summary>
    public Func<string, IReadOnlyCollection<string>, SecurityWarrantConstraints>? ConstraintsFactory { get; set; }

    /// <summary>Optional resource scope for the context. Default: the tenant id.</summary>
    public Func<string, IReadOnlyCollection<string>, string>? ResourceScopeResolver { get; set; }

    /// <summary>
    /// Public keys the Foundgine runtime trusts to verify warrants, keyed by warrant key id (PEM text or a path to a
    /// PEM file). The host's own signing key is always trusted under <see cref="WarrantKeyId"/>; list the other
    /// hosts' key ids here (for example one per worker) so each can be revoked independently.
    /// </summary>
    public Dictionary<string, string> TrustedWarrantPublicKeys { get; } = new(StringComparer.Ordinal);

    // ---- Workers / service accounts ----

    /// <summary>Roles whose grants a Temporal worker acts with. Keep least-privilege.</summary>
    public List<string> WorkerRoles { get; } = [];
    public TimeSpan WorkerWarrantLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public string? ServiceClientId { get; set; }
    public string? ServiceClientSecret { get; set; }

    // ---- Keycloak Admin API (customer onboarding operations) ----

    /// <summary>Admin API base, e.g. <c>https://id.example.com/admin/realms/foundgine</c>. Default: derived from <see cref="Authority"/>.</summary>
    public string? AdminBaseUrl { get; set; }

    /// <summary>Realm roles the admin operations may assign. Anything else in a payload is rejected, so a workflow payload can never grant a privileged role.</summary>
    public List<string> AdminAssignableRoles { get; } = [];

    /// <summary>Required actions the admin operations may set on a new user.</summary>
    public List<string> AdminAllowedRequiredActions { get; } = ["UPDATE_PASSWORD", "VERIFY_EMAIL", "CONFIGURE_TOTP"];

    internal void ValidateWarrantSettings()
    {
        if (string.IsNullOrWhiteSpace(WarrantIssuer)) throw new ArgumentException("WarrantIssuer is required.");
        if (string.IsNullOrWhiteSpace(WarrantAudience)) throw new ArgumentException("WarrantAudience is required.");
        if (string.IsNullOrWhiteSpace(WarrantKeyId)) throw new ArgumentException("WarrantKeyId is required.");
        if (WarrantLifetime < TimeSpan.FromSeconds(1) || WarrantLifetime > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(WarrantLifetime), "Warrants must live between 1 second and 1 hour.");
        if (WorkerWarrantLifetime < TimeSpan.FromSeconds(1) || WorkerWarrantLifetime > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(WorkerWarrantLifetime));
        if (RoleGrants.Count == 0) throw new ArgumentException("RoleGrants must map at least one Keycloak role to grants.");
        foreach (var (role, grants) in RoleGrants)
            foreach (var g in grants)
            {
                if (string.IsNullOrWhiteSpace(g.Capability) || string.IsNullOrWhiteSpace(g.Operation))
                    throw new ArgumentException($"Role '{role}' has a grant without Capability/Operation.");
                // The runtime matches resource scopes by exact string (requireResourceScopeMatch = true), so "*" is a
                // literal that never matches a real scope: it would silently deny everything. An EMPTY list means
                // "any scope"; tenant isolation still comes from the warrant's AllowedTenants constraint.
                if (g.ResourceScopes.Any(r => string.IsNullOrWhiteSpace(r) || r == "*"))
                    throw new ArgumentException(
                        $"Role '{role}' grants {g.Capability}:{g.Operation} with ResourceScopes containing '*' or a blank entry. " +
                        "'*' is not a wildcard; leave ResourceScopes empty for any scope within the tenant, or list exact scopes.");
            }
    }

    internal void ValidateAuthenticationSettings()
    {
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var uri))
            throw new ArgumentException("Authority must be an absolute URL.", nameof(Authority));
        if (RequireHttpsMetadata && uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Authority must be https unless RequireHttpsMetadata is false (development only).");
        if (string.IsNullOrWhiteSpace(Audience)) throw new ArgumentException("Audience is required.", nameof(Audience));
        if (string.IsNullOrWhiteSpace(TenantClaim)) throw new ArgumentException("TenantClaim is required.", nameof(TenantClaim));
    }
}
