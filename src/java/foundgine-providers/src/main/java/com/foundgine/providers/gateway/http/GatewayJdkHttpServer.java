package com.foundgine.providers.gateway.http;

import com.foundgine.core.execution.CancellationTokenSource;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import java.io.IOException;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.util.Locale;
import java.util.Objects;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ThreadFactory;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.function.Consumer;

/**
 * Hosts a {@link GatewayHttpRouter} on the JDK's built-in {@link HttpServer}: the Java counterpart of running
 * {@code MapFoundgineGateway} on Kestrel. The adapter only translates between {@link HttpExchange} and the
 * framework-neutral request/response types; routing, authorization, limits and the SSE protocol stay in the router and
 * handlers, so the behaviour is identical on any other host.
 *
 * <p>Notes for hosts:
 * <ul>
 *   <li>Binds to the loopback interface unless a host is given. Put a TLS-terminating proxy in front for anything else:
 *       {@code HttpServer} speaks plain HTTP.</li>
 *   <li>Each request, including each long-lived SSE stream, occupies one worker thread. Size the executor for the
 *       number of concurrent streams you want to allow.</li>
 *   <li>The JDK server has no disconnect notification. A closed client is noticed on the next write (SSE keep-alives
 *       are written every heartbeat interval), which cancels the request token and releases the subscription.</li>
 *   <li>{@link #stop(int)} cancels every in-flight request token so SSE streams end promptly.</li>
 *   <li>Request attributes {@link #ATTR_REMOTE_ADDRESS} and {@link #ATTR_PRINCIPAL} are exposed to a
 *       {@link GatewayHttpAuthorizer}. Request-scoped state set by an authorizer (for example a security-context
 *       holder) can be cleaned up with {@link Builder#afterRequest}, which runs on the worker thread.</li>
 * </ul>
 */
public final class GatewayJdkHttpServer implements AutoCloseable {
    /** Request attribute: the peer's {@link InetSocketAddress}. */
    public static final String ATTR_REMOTE_ADDRESS = "foundgine.remoteAddress";
    /** Request attribute: the {@link com.sun.net.httpserver.HttpPrincipal} when a server authenticator set one. */
    public static final String ATTR_PRINCIPAL = "foundgine.principal";

    private final GatewayHttpRouter router;
    private final HttpServer server;
    private final ExecutorService executor;
    private final boolean ownsExecutor;
    private final Runnable afterRequest;
    private final Consumer<Throwable> onError;
    private final Set<CancellationTokenSource> inFlight = ConcurrentHashMap.newKeySet();
    private volatile boolean started;
    private volatile boolean stopped;

    private GatewayJdkHttpServer(Builder b) throws IOException {
        router = Objects.requireNonNull(b.router, "router");
        afterRequest = b.afterRequest;
        onError = b.onError;
        if (b.executor == null) { executor = newWorkerPool(b.maxThreads); ownsExecutor = true; }
        else { executor = b.executor; ownsExecutor = false; }
        server = HttpServer.create(new InetSocketAddress(b.host, b.port), b.backlog);
        server.setExecutor(executor);
        server.createContext("/", this::handle);
    }

    public static Builder builder(GatewayHttpRouter router) { return new Builder(router); }

    /** Starts accepting connections. */
    public GatewayJdkHttpServer start() {
        if (stopped) throw new IllegalStateException("The server has been stopped.");
        started = true;
        server.start();
        return this;
    }

    /** The bound port; useful when the server was created with port 0. */
    public int port() { return server.getAddress().getPort(); }

    public InetSocketAddress address() { return server.getAddress(); }

    /**
     * Stops the server. In-flight requests are cancelled first (ending SSE streams), then the server waits up to
     * {@code graceSeconds} for exchanges to finish before closing the remaining connections.
     */
    public void stop(int graceSeconds) {
        if (stopped) return;
        stopped = true;
        for (var source : inFlight) source.cancel();
        if (started) server.stop(Math.max(0, graceSeconds));
        if (ownsExecutor) {
            executor.shutdown();
            try { if (!executor.awaitTermination(Math.max(1, graceSeconds), TimeUnit.SECONDS)) executor.shutdownNow(); }
            catch (InterruptedException interrupted) { executor.shutdownNow(); Thread.currentThread().interrupt(); }
        }
    }

    @Override public void close() { stop(1); }

    // ---- exchange translation --------------------------------------------------------------------------------

    private void handle(HttpExchange exchange) {
        try (var cancel = new CancellationTokenSource()) {
            inFlight.add(cancel);
            try {
                if (stopped) cancel.cancel();
                var response = new ExchangeResponse(exchange, cancel);
                try {
                    var request = toRequest(exchange, cancel);
                    router.dispatch(request, response);
                } catch (IllegalArgumentException malformed) {
                    // An unparsable request line/query never reaches a handler.
                    if (!response.isCommitted()) response.status(400);
                }
                response.finish();
            } catch (IOException clientGone) {
                cancel.cancel();
            } catch (RuntimeException unexpected) {
                cancel.cancel();
                report(unexpected);
            } finally {
                inFlight.remove(cancel);
                try { if (afterRequest != null) afterRequest.run(); } catch (RuntimeException e) { report(e); }
                exchange.close();
            }
        }
    }

    private static GatewayHttpRequest toRequest(HttpExchange exchange, CancellationTokenSource cancel) {
        var uri = exchange.getRequestURI();
        var path = uri.getRawPath();
        if (path == null || !path.startsWith("/")) throw new IllegalArgumentException("Unsupported request target.");
        var builder = GatewayHttpRequest.builder(exchange.getRequestMethod(), path).cancellation(cancel.token());

        for (var header : exchange.getRequestHeaders().entrySet())
            for (var value : header.getValue()) builder.header(header.getKey(), value);

        var rawQuery = uri.getRawQuery();
        if (rawQuery != null && !rawQuery.isEmpty()) {
            for (var pair : rawQuery.split("&")) {
                if (pair.isEmpty()) continue;
                int eq = pair.indexOf('=');
                var name = eq < 0 ? pair : pair.substring(0, eq);
                var value = eq < 0 ? "" : pair.substring(eq + 1);
                // Query strings are form-encoded ('+' is a space), unlike path segments.
                builder.query(URLDecoder.decode(name, StandardCharsets.UTF_8), URLDecoder.decode(value, StandardCharsets.UTF_8));
            }
        }

        // Content-Length is declared by the client; with chunked/unknown length the handler counts bytes itself.
        long length = -1;
        var declared = exchange.getRequestHeaders().getFirst("Content-Length");
        if (declared != null) {
            try { length = Long.parseLong(declared.trim()); } catch (NumberFormatException bad) { throw new IllegalArgumentException("Invalid Content-Length."); }
            if (length < 0) throw new IllegalArgumentException("Invalid Content-Length.");
        }
        builder.body(exchange.getRequestBody(), length);

        builder.attribute(ATTR_REMOTE_ADDRESS, exchange.getRemoteAddress());
        if (exchange.getPrincipal() != null) builder.attribute(ATTR_PRINCIPAL, exchange.getPrincipal());
        return builder.build();
    }

    private void report(Throwable failure) {
        if (onError == null) return;
        try { onError.accept(failure); } catch (RuntimeException ignored) { /* logging must not break the server */ }
    }

    private static ExecutorService newWorkerPool(int maxThreads) {
        var counter = new AtomicInteger();
        ThreadFactory factory = task -> {
            var thread = new Thread(task, "foundgine-gateway-" + counter.incrementAndGet());
            thread.setDaemon(true);
            return thread;
        };
        return Executors.newFixedThreadPool(maxThreads, factory);
    }

    /** Response sink over one exchange. Status/headers are buffered until {@link #body()} commits them. */
    private static final class ExchangeResponse implements GatewayHttpResponse {
        private final HttpExchange exchange;
        private final CancellationTokenSource cancel;
        private int status = 200;
        private boolean committed;
        private OutputStream out;

        ExchangeResponse(HttpExchange exchange, CancellationTokenSource cancel) { this.exchange = exchange; this.cancel = cancel; }

        @Override public synchronized void status(int code) { if (!committed) status = code; }

        @Override public synchronized void header(String name, String value) {
            if (committed) return;
            // The server frames the body itself; a handler must not override framing.
            var lower = name.toLowerCase(Locale.ROOT);
            if (lower.equals("content-length") || lower.equals("transfer-encoding") || lower.equals("connection")) return;
            exchange.getResponseHeaders().set(name, value);
        }

        @Override public synchronized boolean isCommitted() { return committed; }

        @Override public synchronized OutputStream body() throws IOException {
            if (out != null) return out;
            commit(0); // 0 = chunked, so events can be flushed as they happen
            out = new CancellingOutputStream(exchange.getResponseBody(), cancel);
            return out;
        }

        /** Completes a response whose handler never wrote a body. */
        synchronized void finish() throws IOException {
            if (!committed) commit(-1); // -1 = no body
            else if (out != null) out.close();
        }

        private void commit(long length) throws IOException {
            committed = true;
            exchange.sendResponseHeaders(status, length);
        }
    }

    /** A failed write means the peer is gone: cancel the request so handlers and subscriptions unwind. */
    private static final class CancellingOutputStream extends OutputStream {
        private final OutputStream delegate;
        private final CancellationTokenSource cancel;

        CancellingOutputStream(OutputStream delegate, CancellationTokenSource cancel) { this.delegate = delegate; this.cancel = cancel; }

        @Override public void write(int b) throws IOException {
            try { delegate.write(b); } catch (IOException e) { cancel.cancel(); throw e; }
        }

        @Override public void write(byte[] b, int off, int len) throws IOException {
            try { delegate.write(b, off, len); } catch (IOException e) { cancel.cancel(); throw e; }
        }

        @Override public void flush() throws IOException {
            try { delegate.flush(); } catch (IOException e) { cancel.cancel(); throw e; }
        }

        @Override public void close() throws IOException { delegate.close(); }
    }

    public static final class Builder {
        private final GatewayHttpRouter router;
        private String host = InetAddress.getLoopbackAddress().getHostAddress();
        private int port = 8080;
        private int backlog = 0;
        private int maxThreads = 64;
        private ExecutorService executor;
        private Runnable afterRequest;
        private Consumer<Throwable> onError;

        private Builder(GatewayHttpRouter router) { this.router = router; }

        /** Interface to bind; defaults to loopback. Use {@code 0.0.0.0} only behind a TLS-terminating proxy. */
        public Builder host(String v) { host = Objects.requireNonNull(v, "host"); return this; }
        /** Port to bind; {@code 0} picks a free port (see {@link GatewayJdkHttpServer#port()}). */
        public Builder port(int v) {
            if (v < 0 || v > 65535) throw new IllegalArgumentException("port must be 0-65535");
            port = v; return this;
        }
        public Builder backlog(int v) { backlog = Math.max(0, v); return this; }
        /** Worker threads for the default pool (each SSE stream holds one). Ignored when an executor is supplied. */
        public Builder maxThreads(int v) {
            if (v < 1) throw new IllegalArgumentException("maxThreads must be at least one");
            maxThreads = v; return this;
        }
        /** Supplies the worker executor (for example virtual threads); the caller keeps ownership. */
        public Builder executor(ExecutorService v) { executor = v; return this; }
        /** Runs on the worker thread after every request, successful or not (clear request-scoped state here). */
        public Builder afterRequest(Runnable v) { afterRequest = v; return this; }
        /** Receives failures the router did not turn into a response (never shown to callers). */
        public Builder onError(Consumer<Throwable> v) { onError = v; return this; }

        public GatewayJdkHttpServer build() throws IOException { return new GatewayJdkHttpServer(this); }
    }
}
