package com.foundgine.providers.gateway;

import com.foundgine.providers.gateway.events.FoundgineGatewayEventHub;
import com.foundgine.providers.gateway.events.FoundgineGatewaySubscription;
import java.util.Optional;

/** Default gateway facade backed by an immutable discovery snapshot and a live event stream. */
public final class FoundgineGateway implements IFoundgineGateway {
    private final FoundgineGatewayDescription description;
    private final FoundgineGatewayEventHub eventHub;

    /** Discovery plus a default in-memory event hub. */
    public FoundgineGateway(FoundgineGatewayOptions options) {
        this(options, new FoundgineGatewayEventHub(requireOptions(options)));
    }
    public FoundgineGateway(FoundgineGatewayOptions options, FoundgineGatewayEventHub eventHub) {
        requireOptions(options);
        if (eventHub == null) throw new IllegalArgumentException("eventHub is required");
        this.eventHub = eventHub;
        description = new FoundgineGatewayDescription(options.name(), options.contractVersion(), options.capabilities());
    }
    private static FoundgineGatewayOptions requireOptions(FoundgineGatewayOptions options) {
        if (options == null) throw new IllegalArgumentException("options is required");
        return options;
    }
    @Override public FoundgineGatewayDescription describe() { return description; }
    @Override public Optional<FoundgineGatewayCapability> findCapability(String id) {
        if (id == null || id.isBlank()) throw new IllegalArgumentException("id is required");
        return description.capabilities().stream().filter(c -> c.id().equalsIgnoreCase(id)).findFirst();
    }
    @Override public FoundgineGatewaySubscription subscribe() { return eventHub.subscribe(); }
    @Override public FoundgineGatewaySubscription subscribeAfter(long lastSequence) { return eventHub.subscribeAfter(lastSequence); }
}
