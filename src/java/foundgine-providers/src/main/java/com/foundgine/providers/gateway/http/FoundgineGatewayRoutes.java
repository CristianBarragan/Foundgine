package com.foundgine.providers.gateway.http;

import java.util.ArrayList;

/** Builds the gateway's route table, the framework-neutral counterpart of {@code MapFoundgineGateway}. */
public final class FoundgineGatewayRoutes {
    private FoundgineGatewayRoutes() { }

    /** Maps the gateway under {@code /foundgine} with discovery endpoints protected. */
    public static GatewayHttpRouter map(FoundgineGatewayHttpServices services) { return map(services, "/foundgine", true); }

    /**
     * @param routePrefix          the route prefix, for example {@code /foundgine}; a bare {@code /} mounts at the root
     * @param requireAuthorization protects the discovery endpoints (capabilities, events, semantic); execution
     *                             endpoints are always protected, even if a host deliberately makes discovery public
     */
    public static GatewayHttpRouter map(FoundgineGatewayHttpServices services, String routePrefix, boolean requireAuthorization) {
        if (services == null) throw new IllegalArgumentException("services is required");
        if (routePrefix == null || routePrefix.isBlank()) throw new IllegalArgumentException("routePrefix is required");
        var prefix = routePrefix.replaceAll("/+$", "");
        if (!routePrefix.startsWith("/")) throw new IllegalArgumentException("The gateway route prefix must start with '/'.");

        var h = new FoundgineGatewayHandlers(services);
        var routes = new ArrayList<GatewayHttpRoute>();
        routes.add(new GatewayHttpRoute("GET", prefix + "/capabilities", requireAuthorization, h::capabilities));
        routes.add(new GatewayHttpRoute("GET", prefix + "/capabilities/{id}", requireAuthorization, h::capability));
        routes.add(new GatewayHttpRoute("GET", prefix + "/events", requireAuthorization, h::streamEvents));
        routes.add(new GatewayHttpRoute("GET", prefix + "/semantic", requireAuthorization, h::semantic));
        routes.add(new GatewayHttpRoute("POST", prefix + "/read", true, h::read));
        routes.add(new GatewayHttpRoute("POST", prefix + "/mutations/dry-run", true, h::dryRun));
        routes.add(new GatewayHttpRoute("POST", prefix + "/mutations/approvals", true, h::createApproval));
        routes.add(new GatewayHttpRoute("POST", prefix + "/mutations/approvals/{handle}/execute", true, h::executeApproved));
        routes.add(new GatewayHttpRoute("DELETE", prefix + "/mutations/approvals/{handle}", true, h::revokeApproval));
        routes.add(new GatewayHttpRoute("GET", prefix + "/executions/{id}", true, h::executionStatus));
        return new GatewayHttpRouter(routes, services.authorizer());
    }
}
