package com.foundgine.providers.gateway.http;

import java.util.Objects;

/** A method + path template (segments such as {@code {id}} bind path parameters) bound to a handler. */
public record GatewayHttpRoute(String method, String template, boolean requireAuthorization, GatewayHttpHandler handler) {
    public GatewayHttpRoute {
        Objects.requireNonNull(method, "method");
        Objects.requireNonNull(handler, "handler");
        if (template == null || !template.startsWith("/")) throw new IllegalArgumentException("template must start with '/'");
        method = method.toUpperCase(java.util.Locale.ROOT);
    }
}
