using System.Security;
using System.Text.RegularExpressions;
using Foundgine.SupplyChain.Advanced.Authorization;

namespace Foundgine.SupplyChain.Advanced.Intent;

/// <summary>
/// The host-owned identity behind an intent request: tenant and role.
/// <para>
/// This is deliberately <em>not</em> part of the intent. The canonical intent says what is wanted;
/// who is asking is decided by the host and handed to the semantic authorization policy. Anything
/// identity-shaped inside an intent body is rejected by the intent adapter as an unknown property.
/// </para>
/// <para>
/// DEMO ONLY: <see cref="Resolve"/> reads the identity from two plain HTTP headers so the sample can
/// show the same intent returning different results per role. A real host derives tenant and role
/// from an authenticated principal (OIDC token, mTLS, signed warrant) and never from a header the
/// client can set. The defaults are the least privileged role and the first fixture tenant, and every
/// response echoes the effective caller so the choice is never silent.
/// </para>
/// </summary>
public sealed record IntentCaller(string TenantId, SupplyChainRole Role)
{
    public const string TenantHeader = "X-Foundgine-Tenant";
    public const string RoleHeader = "X-Foundgine-Role";
    public const string DefaultTenant = "tenant-a";
    public const SupplyChainRole DefaultRole = SupplyChainRole.Customer;

    // \z, not $: in .NET "$" also matches before a trailing newline.
    private static readonly Regex TenantPattern =
        new(@"^[a-z0-9][a-z0-9-]{0,31}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The role as the wire spells it: <c>ANALYST</c>, <c>WAREHOUSE_OPERATOR</c>, ...</summary>
    public string RoleName => RoleWireName(Role);

    /// <param name="tenantHeader">Value of <see cref="TenantHeader"/>, or null/blank for the default.</param>
    /// <param name="roleHeader">Value of <see cref="RoleHeader"/>, or null/blank for the default.</param>
    /// <param name="knownTenants">
    /// Tenants that exist in the data. An unknown tenant is refused rather than treated as an empty
    /// tenant, and it also bounds anything keyed by tenant.
    /// </param>
    /// <exception cref="SecurityException">For an unknown tenant or role.</exception>
    public static IntentCaller Resolve(string? tenantHeader, string? roleHeader, IReadOnlySet<string> knownTenants)
    {
        ArgumentNullException.ThrowIfNull(knownTenants);

        var tenant = IsBlank(tenantHeader) ? DefaultTenant : tenantHeader!.Trim();
        if (!TenantPattern.IsMatch(tenant) || !knownTenants.Contains(tenant))
            throw new SecurityException($"Unknown tenant for header {TenantHeader}.");

        var role = DefaultRole;
        if (!IsBlank(roleHeader))
        {
            var wanted = Normalize(roleHeader!);
            var found = false;
            // Compare names instead of Enum.TryParse, which would also accept "1" or "Analyst,Customer".
            foreach (var candidate in Enum.GetValues<SupplyChainRole>())
            {
                if (Normalize(candidate.ToString()) != wanted) continue;
                role = candidate;
                found = true;
                break;
            }

            if (!found)
                throw new SecurityException(
                    $"Unknown role for header {RoleHeader}. Expected one of " +
                    $"[{string.Join(", ", Enum.GetValues<SupplyChainRole>().Select(RoleWireName))}].");
        }

        return new IntentCaller(tenant, role);
    }

    private static string Normalize(string value) =>
        value.Trim().Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal)
            .ToUpperInvariant();

    private static string RoleWireName(SupplyChainRole role) =>
        Regex.Replace(role.ToString(), "(?<=[a-z])(?=[A-Z])", "_", RegexOptions.CultureInvariant).ToUpperInvariant();

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);
}
