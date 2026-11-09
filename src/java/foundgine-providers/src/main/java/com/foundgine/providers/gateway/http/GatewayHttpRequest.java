package com.foundgine.providers.gateway.http;

import com.foundgine.core.execution.CancellationToken;
import java.io.ByteArrayInputStream;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Objects;

/**
 * Framework-neutral view of one inbound HTTP request. Host adapters (JDK {@code HttpServer}, servlet, ...) build one
 * per request. Header names are case-insensitive; query values are already percent-decoded; {@link #path()} is the
 * raw (still percent-encoded) request path so the router can decode each segment itself.
 */
public final class GatewayHttpRequest {
    private final String method;
    private final String path;
    private final Map<String, List<String>> query;
    private final Map<String, List<String>> headers;
    private final InputStream body;
    private final long contentLength;
    private final CancellationToken cancellation;
    private final Map<String, String> pathParams;
    private final Map<String, Object> attributes;

    private GatewayHttpRequest(Builder b, Map<String, String> pathParams) {
        this.method = b.method;
        this.path = b.path;
        this.query = b.query;
        this.headers = b.headers;
        this.body = b.body;
        this.contentLength = b.contentLength;
        this.cancellation = b.cancellation;
        this.attributes = b.attributes;
        this.pathParams = pathParams;
    }

    public static Builder builder(String method, String path) { return new Builder(method, path); }

    public String method() { return method; }
    public String path() { return path; }
    public InputStream body() { return body; }
    /** Declared Content-Length, or -1 when unknown (chunked). */
    public long contentLength() { return contentLength; }
    /** Cancelled by the host adapter when the client disconnects or the host is stopping. */
    public CancellationToken cancellation() { return cancellation; }
    /** Host-supplied data (for example an authenticated principal) for {@link GatewayHttpAuthorizer} implementations. */
    public Object attribute(String name) { return attributes.get(name); }

    /** First value of the header, or {@code null}. */
    public String header(String name) {
        var values = headers.get(name.toLowerCase(Locale.ROOT));
        return values == null || values.isEmpty() ? null : values.get(0);
    }

    public boolean hasHeader(String name) { return headers.containsKey(name.toLowerCase(Locale.ROOT)); }

    /** First value of the query parameter, or {@code null}. */
    public String queryParam(String name) {
        var values = query.get(name);
        return values == null || values.isEmpty() ? null : values.get(0);
    }

    public String pathParam(String name) { return pathParams.get(name); }

    GatewayHttpRequest withPathParams(Map<String, String> params) {
        var copy = new Builder(method, path);
        copy.query = query; copy.headers = headers; copy.body = body; copy.contentLength = contentLength;
        copy.cancellation = cancellation; copy.attributes = attributes;
        return new GatewayHttpRequest(copy, Map.copyOf(params));
    }

    public static final class Builder {
        private final String method;
        private final String path;
        private Map<String, List<String>> query = new LinkedHashMap<>();
        private Map<String, List<String>> headers = new LinkedHashMap<>();
        private InputStream body = InputStream.nullInputStream();
        private long contentLength = -1;
        private CancellationToken cancellation = CancellationToken.NONE;
        private Map<String, Object> attributes = new LinkedHashMap<>();

        private Builder(String method, String path) {
            if (method == null || method.isBlank()) throw new IllegalArgumentException("method is required");
            if (path == null || !path.startsWith("/")) throw new IllegalArgumentException("path must start with '/'");
            this.method = method.toUpperCase(Locale.ROOT);
            this.path = path;
        }

        public Builder query(String name, String value) {
            query.computeIfAbsent(name, k -> new ArrayList<>()).add(value == null ? "" : value);
            return this;
        }

        public Builder header(String name, String value) {
            headers.computeIfAbsent(name.toLowerCase(Locale.ROOT), k -> new ArrayList<>()).add(value == null ? "" : value);
            return this;
        }

        public Builder body(InputStream stream, long contentLength) {
            this.body = Objects.requireNonNull(stream, "stream");
            this.contentLength = contentLength;
            return this;
        }

        public Builder body(byte[] bytes) {
            Objects.requireNonNull(bytes, "bytes");
            this.body = new ByteArrayInputStream(bytes);
            this.contentLength = bytes.length;
            return this;
        }

        public Builder cancellation(CancellationToken token) {
            this.cancellation = Objects.requireNonNull(token, "token");
            return this;
        }

        public Builder attribute(String name, Object value) { attributes.put(name, value); return this; }

        public GatewayHttpRequest build() { return new GatewayHttpRequest(this, Map.of()); }
    }
}
