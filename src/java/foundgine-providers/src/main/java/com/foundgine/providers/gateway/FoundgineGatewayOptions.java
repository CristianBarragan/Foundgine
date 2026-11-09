package com.foundgine.providers.gateway;

import com.foundgine.providers.gateway.events.FoundgineGatewayEventStreamOptions;
import java.util.ArrayList;
import java.util.List;

/** Configuration for the gateway discovery facade and event pipeline. */
public final class FoundgineGatewayOptions {
    private String name = "Foundgine Gateway";
    private String contractVersion = "1.0";
    private int subscriberBufferCapacity = 256;
    private int replayPageSize = 1_000;
    private final FoundgineGatewayEventStreamOptions eventStream = new FoundgineGatewayEventStreamOptions();
    private final List<FoundgineGatewayCapability> capabilities = new ArrayList<>();

    public FoundgineGatewayOptions name(String value) {
        if (value == null || value.isBlank()) throw new IllegalArgumentException("name is required");
        name = value; return this;
    }
    public FoundgineGatewayOptions contractVersion(String value) {
        if (value == null || value.isBlank()) throw new IllegalArgumentException("contractVersion is required");
        contractVersion = value; return this;
    }
    /** Maximum number of recent events retained per live subscriber (oldest dropped first). */
    public FoundgineGatewayOptions subscriberBufferCapacity(int value) {
        if (value < 1) throw new IllegalArgumentException("subscriberBufferCapacity must be at least one");
        subscriberBufferCapacity = value; return this;
    }
    /** Events read from the journal per round trip while replaying or back-filling (1-10,000). */
    public FoundgineGatewayOptions replayPageSize(int value) {
        if (value < 1 || value > 10_000) throw new IllegalArgumentException("replayPageSize must be 1-10,000");
        replayPageSize = value; return this;
    }
    public FoundgineGatewayOptions addCapability(FoundgineGatewayCapability capability) {
        if (capability == null) throw new IllegalArgumentException("capability is required");
        if (capabilities.stream().anyMatch(c -> c.id().equalsIgnoreCase(capability.id())))
            throw new IllegalStateException("Gateway capability '" + capability.id() + "' is already registered");
        capabilities.add(capability); return this;
    }
    public String name() { return name; }
    public String contractVersion() { return contractVersion; }
    public int subscriberBufferCapacity() { return subscriberBufferCapacity; }
    public int replayPageSize() { return replayPageSize; }
    public FoundgineGatewayEventStreamOptions eventStream() { return eventStream; }
    public List<FoundgineGatewayCapability> capabilities() { return List.copyOf(capabilities); }
}
