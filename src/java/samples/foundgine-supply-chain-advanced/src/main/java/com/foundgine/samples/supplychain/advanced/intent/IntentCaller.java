package com.foundgine.samples.supplychain.advanced.intent;

import com.foundgine.samples.supplychain.advanced.authorization.SupplyChainAuthorization.Role;

import java.util.Locale;
import java.util.Set;
import java.util.regex.Pattern;

/**
 * The host-owned identity behind an intent request: tenant and role.
 *
 * <p>This is deliberately <em>not</em> part of the intent. The canonical intent says what is wanted;
 * who is asking is decided by the host and handed to the semantic authorization policy. Anything
 * identity-shaped inside an intent body is rejected by the intent adapter as an unknown property.
 *
 * <p>DEMO ONLY: {@link #resolve} reads the identity from two plain HTTP headers so the sample can
 * show the same intent returning different results per role. A real host derives tenant and role
 * from an authenticated principal (OIDC token, mTLS, signed warrant) and never from a header the
 * client can set. The defaults are the least privileged role and the first fixture tenant, and every
 * response echoes the effective caller so the choice is never silent.
 */
public record IntentCaller(String tenantId, Role role) {
    public static final String TENANT_HEADER = "X-Foundgine-Tenant";
    public static final String ROLE_HEADER = "X-Foundgine-Role";
    public static final String DEFAULT_TENANT = "tenant-a";
    public static final Role DEFAULT_ROLE = Role.CUSTOMER;

    private static final Pattern TENANT = Pattern.compile("[a-z0-9][a-z0-9-]{0,31}");

    /**
     * @param tenantHeader value of {@link #TENANT_HEADER}, or {@code null}/blank for the default
     * @param roleHeader value of {@link #ROLE_HEADER}, or {@code null}/blank for the default
     * @param knownTenants tenants that exist in the data; an unknown tenant is refused rather than
     *     treated as an empty tenant, and it also bounds anything keyed by tenant
     * @throws SecurityException for an unknown tenant or role
     */
    public static IntentCaller resolve(String tenantHeader, String roleHeader, Set<String> knownTenants) {
        String tenant = blank(tenantHeader) ? DEFAULT_TENANT : tenantHeader.strip();
        if (!TENANT.matcher(tenant).matches() || !knownTenants.contains(tenant)) {
            throw new SecurityException("Unknown tenant for header " + TENANT_HEADER + ".");
        }
        Role role = DEFAULT_ROLE;
        if (!blank(roleHeader)) {
            try {
                role = Role.valueOf(roleHeader.strip().toUpperCase(Locale.ROOT).replace('-', '_'));
            } catch (IllegalArgumentException e) {
                throw new SecurityException(
                        "Unknown role for header "
                                + ROLE_HEADER
                                + ". Expected one of "
                                + java.util.Arrays.toString(Role.values())
                                + ".");
            }
        }
        return new IntentCaller(tenant, role);
    }

    private static boolean blank(String value) {
        return value == null || value.isBlank();
    }
}
