package com.foundgine.providers.gateway.events;

/** Reserved attribute keys used for audience scoping. They never leave the server. */
public final class FoundgineGatewayEventAttributes {
    private FoundgineGatewayEventAttributes() { }
    /** Event is visible only to the principal (tenant + subject) whose digest it carries. */
    public static final String OWNER_DIGEST = "owner.digest";
    /** Event is visible to every principal of the tenant whose digest it carries. */
    public static final String TENANT_DIGEST = "tenant.digest";
    static boolean isReserved(String key) { return OWNER_DIGEST.equals(key) || TENANT_DIGEST.equals(key); }
}
