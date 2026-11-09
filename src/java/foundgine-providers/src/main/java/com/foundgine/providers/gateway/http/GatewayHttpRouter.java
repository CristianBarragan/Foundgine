package com.foundgine.providers.gateway.http;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CancellationException;

/**
 * Dispatches a {@link GatewayHttpRequest} to a route: 404 for unknown paths, 405 (with {@code Allow}) for a known
 * path with the wrong method, authorization before the handler, and 500 for failures that escape a handler.
 */
public final class GatewayHttpRouter {
    private final List<GatewayHttpRoute> routes;
    private final GatewayHttpAuthorizer authorizer;
    private java.util.function.Consumer<RuntimeException> onUnhandled;

    public GatewayHttpRouter(List<GatewayHttpRoute> routes, GatewayHttpAuthorizer authorizer) {
        this.routes = List.copyOf(routes);
        this.authorizer = authorizer;
        // Fail closed at construction: a protected route with nobody to authorize it would otherwise be open.
        if (authorizer == null && this.routes.stream().anyMatch(GatewayHttpRoute::requireAuthorization))
            throw new IllegalStateException("The gateway has protected routes; a GatewayHttpAuthorizer is required.");
    }

    public List<GatewayHttpRoute> routes() { return routes; }

    public void dispatch(GatewayHttpRequest request, GatewayHttpResponse response) {
        try {
            var allowed = new LinkedHashSet<String>();
            for (var route : routes) {
                var params = match(route.template(), request.path());
                if (params == null) continue;
                if (!route.method().equals(request.method())) { allowed.add(route.method()); continue; }
                var bound = request.withPathParams(params);
                if (route.requireAuthorization()) {
                    var decision = authorizer.authorize(bound);
                    if (decision == GatewayHttpAuthorizer.Decision.UNAUTHENTICATED) { response.status(401); return; }
                    if (decision != GatewayHttpAuthorizer.Decision.ALLOWED) { response.status(403); return; }
                }
                route.handler().handle(bound, response);
                return;
            }
            if (allowed.isEmpty()) { response.status(404); return; }
            response.status(405);
            response.header("Allow", String.join(", ", allowed));
        } catch (CancellationException | UncheckedIOException clientGone) {
            // The client disconnected or the host is stopping; nothing to report.
        } catch (IOException clientGone) {
            // Same: a failed write means the peer is gone.
        } catch (Exception failure) {
            // Never leak exception detail to the caller; hosts log through their own adapter.
            if (!response.isCommitted()) response.status(500);
            if (failure instanceof RuntimeException re && onUnhandled != null) onUnhandled.accept(re);
        }
    }

    /** Optional hook so a host can log failures that became a 500. */
    public GatewayHttpRouter onUnhandled(java.util.function.Consumer<RuntimeException> sink) { this.onUnhandled = sink; return this; }

    /** Returns the bound path parameters, or {@code null} when the path does not match the template. */
    static Map<String, String> match(String template, String rawPath) {
        var t = split(template);
        var p = split(rawPath);
        if (t.size() != p.size()) return null;
        var params = new LinkedHashMap<String, String>();
        for (int i = 0; i < t.size(); i++) {
            var seg = t.get(i);
            if (seg.length() > 2 && seg.startsWith("{") && seg.endsWith("}")) {
                String value;
                try { value = percentDecode(p.get(i)); } catch (IllegalArgumentException bad) { return null; }
                if (value.isEmpty()) return null;
                params.put(seg.substring(1, seg.length() - 1), value);
            } else if (!seg.equals(p.get(i))) return null;
        }
        return params;
    }

    private static List<String> split(String path) {
        var out = new ArrayList<String>();
        for (var s : path.split("/", -1)) if (!s.isEmpty()) out.add(s);
        return out;
    }

    /** Percent-decoding for path segments ('+' stays '+'); rejects malformed escapes and invalid UTF-8. */
    static String percentDecode(String s) {
        if (s.indexOf('%') < 0) return s;
        var bytes = new java.io.ByteArrayOutputStream(s.length());
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            if (c == '%') {
                if (i + 2 >= s.length()) throw new IllegalArgumentException("Malformed percent escape.");
                int hi = Character.digit(s.charAt(i + 1), 16), lo = Character.digit(s.charAt(i + 2), 16);
                if (hi < 0 || lo < 0) throw new IllegalArgumentException("Malformed percent escape.");
                bytes.write((hi << 4) | lo);
                i += 2;
            } else {
                var chunk = String.valueOf(c);
                if (Character.isHighSurrogate(c) && i + 1 < s.length()) chunk = s.substring(i, ++i + 1);
                bytes.writeBytes(chunk.getBytes(StandardCharsets.UTF_8));
            }
        }
        try {
            return StandardCharsets.UTF_8.newDecoder()
                    .onMalformedInput(java.nio.charset.CodingErrorAction.REPORT)
                    .onUnmappableCharacter(java.nio.charset.CodingErrorAction.REPORT)
                    .decode(java.nio.ByteBuffer.wrap(bytes.toByteArray())).toString();
        } catch (java.nio.charset.CharacterCodingException e) { throw new IllegalArgumentException("Invalid UTF-8 in path.", e); }
    }
}
