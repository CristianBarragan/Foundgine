package com.foundgine.providers.gateway.http;

import java.io.IOException;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;

/** Server-Sent Events framing. Every frame is flushed so intermediaries and clients see it immediately. */
public final class GatewaySseWriter {
    private final OutputStream out;

    public GatewaySseWriter(OutputStream out) { this.out = out; }

    /** {@code retry: <ms>} frame telling the client how long to wait before reconnecting. */
    public void retry(long milliseconds) throws IOException { write("retry: " + milliseconds + "\n\n"); }

    /** A data frame. A multi-line {@code data} is split across {@code data:} lines as the SSE format requires. */
    public void event(long id, String type, String data) throws IOException {
        var sb = new StringBuilder(data.length() + 48);
        sb.append("id: ").append(id).append('\n');
        sb.append("event: ").append(type).append('\n');
        for (var line : data.split("\r\n|\r|\n", -1)) sb.append("data: ").append(line).append('\n');
        write(sb.append('\n').toString());
    }

    /**
     * Keep-alive comment. When {@code checkpointId} is non-null the {@code id:} line moves the client's resume cursor
     * past events it may not see, so a quiet tenant on a busy gateway never falls outside the replay window.
     */
    public void keepAlive(Long checkpointId) throws IOException {
        write(": keepalive\n" + (checkpointId == null ? "" : "id: " + checkpointId + "\n") + "\n");
    }

    private void write(String text) throws IOException {
        out.write(text.getBytes(StandardCharsets.UTF_8));
        out.flush();
    }
}
