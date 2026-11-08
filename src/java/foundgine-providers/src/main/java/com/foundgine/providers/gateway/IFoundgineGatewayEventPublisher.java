package com.foundgine.providers.gateway;
import java.util.Map;
/** Publishes lifecycle events; implementations may persist and fan out events. */
@FunctionalInterface
public interface IFoundgineGatewayEventPublisher {
    void publish(FoundgineGatewayEventType type, String subject, String correlationId, String providerId, Map<String, String> attributes);
}
