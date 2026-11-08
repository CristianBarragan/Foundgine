package com.foundgine.providers.gateway.http;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.providers.gateway.semantics.FoundgineGatewaySemanticDescription;

/**
 * Writes the semantic description with Jackson. The capability contract is a deep graph owned by foundgine-core,
 * whose types already carry their own Jackson (de)serializers (for example the unsigned-64-bit {@code EntityId}),
 * so this is the one gateway response that is not hand-written. Native-image users must register those types.
 */
final class FoundgineGatewaySemanticJson {
    private static final ObjectMapper MAPPER = new ObjectMapper();
    private FoundgineGatewaySemanticJson() { }

    static String toJson(FoundgineGatewaySemanticDescription description) {
        try { return MAPPER.writeValueAsString(description); }
        catch (JsonProcessingException e) { throw new IllegalStateException("Semantic description could not be serialized.", e); }
    }
}
