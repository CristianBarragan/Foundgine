package com.foundgine.providers.gateway.http;

/** One endpoint. Unchecked failures that escape become a 500 unless the response is already committed. */
@FunctionalInterface
public interface GatewayHttpHandler {
    void handle(GatewayHttpRequest request, GatewayHttpResponse response) throws Exception;
}
