package com.foundgine.providers.gateway.events;

import com.foundgine.providers.gateway.FoundgineGatewayEvent;
import com.foundgine.providers.gateway.FoundgineGatewayEventType;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.LinkedHashMap;

/**
 * Fail-closed audience rules: owner-scoped events reach only that owner; tenant-scoped events reach that tenant;
 * discovery-change events reach every authenticated caller; every other unscoped event is operators-only.
 * Operators do not see other tenants' scoped events.
 */
public final class FoundgineGatewayEventVisibility {
    private FoundgineGatewayEventVisibility() { }

    public static boolean isVisible(FoundgineGatewayEvent gatewayEvent, FoundgineGatewayViewer viewer) {
        if (gatewayEvent == null) throw new IllegalArgumentException("gatewayEvent is required");
        if (viewer == null) throw new IllegalArgumentException("viewer is required");
        var attrs = gatewayEvent.attributes();
        var owner = attrs.get(FoundgineGatewayEventAttributes.OWNER_DIGEST);
        if (owner != null) return equal(owner, viewer.ownerDigest());
        var tenant = attrs.get(FoundgineGatewayEventAttributes.TENANT_DIGEST);
        if (tenant != null) return equal(tenant, viewer.tenantDigest());
        return gatewayEvent.type() == FoundgineGatewayEventType.CAPABILITY_CHANGED
                || gatewayEvent.type() == FoundgineGatewayEventType.SEMANTIC_MODEL_CHANGED
                || viewer.isOperator();
    }

    /** Removes reserved scoping attributes before an event is serialized to a client. */
    public static FoundgineGatewayEvent toPublic(FoundgineGatewayEvent gatewayEvent) {
        if (gatewayEvent == null) throw new IllegalArgumentException("gatewayEvent is required");
        if (gatewayEvent.attributes().keySet().stream().noneMatch(FoundgineGatewayEventAttributes::isReserved))
            return gatewayEvent;
        var kept = new LinkedHashMap<String, String>();
        gatewayEvent.attributes().forEach((k, v) -> { if (!FoundgineGatewayEventAttributes.isReserved(k)) kept.put(k, v); });
        return gatewayEvent.withAttributes(kept);
    }

    private static boolean equal(String a, String b) {
        if (a == null || b == null) return false;
        return MessageDigest.isEqual(a.getBytes(StandardCharsets.UTF_8), b.getBytes(StandardCharsets.UTF_8));
    }
}
