package com.foundgine.providers.gateway;
/** Built-in, domain-neutral gateway lifecycle event categories. */
public enum FoundgineGatewayEventType {
    CAPABILITY_CHANGED, SEMANTIC_MODEL_CHANGED, PROVIDER_HEALTH_CHANGED, EXECUTION_STARTED,
    EXECUTION_COMPLETED, EXECUTION_FAILED, AUTHORIZATION_DENIED, PROVIDER_REGISTERED,
    PROVIDER_UNREGISTERED, CUSTOM
}
