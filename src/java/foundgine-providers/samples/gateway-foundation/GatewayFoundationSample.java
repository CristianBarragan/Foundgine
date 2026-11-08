package com.foundgine.providers.gateway;

import java.util.List;
import java.util.Map;

/** Runnable, dependency-free example for the Java Gateway foundation. */
public final class GatewayFoundationSample {
    private GatewayFoundationSample() { }

    public static void main(String[] args) {
        var options = new FoundgineGatewayOptions()
                .name("Supply Chain Gateway")
                .addCapability(new FoundgineGatewayCapability("orders.read", "Order read", "1.0",
                        "query", "Read order data", List.of("get", "list"), Map.of()));
        IFoundgineGateway gateway = new FoundgineGateway(options);
        System.out.println(gateway.describe());

        IFoundgineGatewayEventPublisher publisher = (type, subject, correlation, provider, attributes) ->
                System.out.println("Lifecycle event: " + type + " subject=" + subject);
        var registry = new FoundgineGatewayProviderRegistry(publisher);
        registry.register(new FoundgineGatewayProviderDescriptor("foundgine.memory.events",
                "In-memory event provider", "1.0", List.of("STREAMING")));
        registry.reportHealth("foundgine.memory.events", FoundgineGatewayProviderHealth.HEALTHY, "Ready");
        System.out.println("Registered providers: " + registry.getSnapshot().size());
    }
}
