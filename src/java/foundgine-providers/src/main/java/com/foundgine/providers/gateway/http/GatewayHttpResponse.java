package com.foundgine.providers.gateway.http;

import java.io.IOException;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;

/**
 * Framework-neutral response sink. Status and headers may be set until {@link #body()} is first called, which commits
 * the response; a handler that never calls {@code body()} produces a response with no content (the adapter finishes it).
 * Writes after the client has gone away fail with an {@link IOException}, which handlers treat as a disconnect.
 */
public interface GatewayHttpResponse {
    void status(int code);

    /** Sets (replaces) a response header. Ignored once the response is committed. */
    void header(String name, String value);

    boolean isCommitted();

    /** Commits the status and headers and returns the unbuffered body stream. */
    OutputStream body() throws IOException;

    default void writeJson(String json) throws IOException {
        header("Content-Type", "application/json; charset=utf-8");
        var out = body();
        out.write(json.getBytes(StandardCharsets.UTF_8));
        out.flush();
    }
}
