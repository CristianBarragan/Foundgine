package com.foundgine.providers.gateway;

import com.foundgine.providers.gateway.events.FoundgineGatewaySubscription;
import java.util.Optional;

/** Stable gateway facade for capability discovery and event subscriptions. */
public interface IFoundgineGateway {
    FoundgineGatewayDescription describe();
    Optional<FoundgineGatewayCapability> findCapability(String id);
    /** Replays retained events, then follows live events. Close the subscription when done. */
    FoundgineGatewaySubscription subscribe();
    /** Replays retained events after {@code lastSequence}, then follows live events. */
    FoundgineGatewaySubscription subscribeAfter(long lastSequence);
}
