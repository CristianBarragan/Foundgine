package com.foundgine.providers.gateway.http;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.providers.gateway.FoundgineGatewayEventType;
import com.foundgine.providers.gateway.http.FoundgineGatewayHttpHandlersTest.Env;
import java.io.BufferedReader;
import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.InputStreamReader;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.LinkedHashMap;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.function.BooleanSupplier;
import org.junit.jupiter.api.Test;

/** The JDK {@code HttpServer} adapter over a real loopback socket, using the same fixtures as the handler tests. */
class GatewayJdkHttpServerTest {
    private static final HttpClient CLIENT = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(5)).build();

    private static GatewayJdkHttpServer start(Env env) throws IOException {
        return GatewayJdkHttpServer.builder(env.router).port(0).build().start();
    }

    private static URI uri(GatewayJdkHttpServer server, String pathAndQuery) {
        return URI.create("http://127.0.0.1:" + server.port() + pathAndQuery);
    }

    private static HttpRequest.Builder request(GatewayJdkHttpServer server, String pathAndQuery) {
        return HttpRequest.newBuilder(uri(server, pathAndQuery)).timeout(Duration.ofSeconds(10)).header("X-User", "alice");
    }

    private static HttpResponse<String> send(HttpRequest request) throws Exception {
        return CLIENT.send(request, HttpResponse.BodyHandlers.ofString());
    }

    /** Sends one raw GET (authenticated as alice) and returns the status code from the response line. */
    private static int rawStatus(GatewayJdkHttpServer server, String target) throws IOException {
        try (var socket = new java.net.Socket("127.0.0.1", server.port())) {
            socket.setSoTimeout(5000);
            var out = socket.getOutputStream();
            out.write(("GET " + target + " HTTP/1.1\r\nHost: localhost\r\nX-User: alice\r\nConnection: close\r\n\r\n")
                    .getBytes(StandardCharsets.US_ASCII));
            out.flush();
            var statusLine = new BufferedReader(new InputStreamReader(socket.getInputStream(), StandardCharsets.US_ASCII)).readLine();
            return Integer.parseInt(statusLine.split(" ")[1]);
        }
    }

    private static void await(BooleanSupplier condition) throws InterruptedException {
        long deadline = System.nanoTime() + Duration.ofSeconds(5).toNanos();
        while (!condition.getAsBoolean()) {
            if (System.nanoTime() > deadline) fail("Timed out waiting for condition");
            Thread.sleep(10);
        }
    }

    @Test void discoveryIsServedAsJsonOverHttp() throws Exception {
        var env = new Env();
        try (var server = start(env)) {
            var res = send(request(server, "/foundgine/capabilities").GET().build());
            assertEquals(200, res.statusCode());
            assertEquals("application/json; charset=utf-8", res.headers().firstValue("Content-Type").orElse(null));
            assertTrue(res.body().contains("\"cap.1\""), res.body());

            var one = send(request(server, "/foundgine/capabilities/cap.1").GET().build());
            assertEquals(200, one.statusCode());
            assertEquals(404, send(request(server, "/foundgine/capabilities/missing").GET().build()).statusCode());
        }
    }

    @Test void routerStatusCodesSurviveTheAdapter() throws Exception {
        var env = new Env();
        try (var server = start(env)) {
            var anonymous = send(HttpRequest.newBuilder(uri(server, "/foundgine/capabilities")).GET().build());
            assertEquals(401, anonymous.statusCode());
            assertEquals("", anonymous.body());
            assertEquals(403, send(HttpRequest.newBuilder(uri(server, "/foundgine/capabilities")).header("X-User", "denied").GET().build()).statusCode());
            assertEquals(404, send(request(server, "/nope").GET().build()).statusCode());
            var wrong = send(request(server, "/foundgine/read").GET().build());
            assertEquals(405, wrong.statusCode());
            assertEquals("POST", wrong.headers().firstValue("Allow").orElse(null));
        }
    }

    @Test void readRoundTripReturnsExecutionIdAndStatusIsQueryable() throws Exception {
        var env = new Env();
        try (var server = start(env)) {
            var res = send(request(server, "/foundgine/read").header("Content-Type", "application/json")
                    .POST(HttpRequest.BodyPublishers.ofString(FoundgineGatewayHttpHandlersTest.READ)).build());
            assertEquals(200, res.statusCode(), res.body());
            var id = res.headers().firstValue("X-Foundgine-Execution-Id").orElseThrow();
            assertEquals(32, id.length());

            var status = send(request(server, "/foundgine/executions/" + id).GET().build());
            assertEquals(200, status.statusCode(), status.body());
            assertTrue(status.body().contains("\"succeeded\""), status.body());
            assertEquals(400, send(request(server, "/foundgine/executions/not-an-id").GET().build()).statusCode());
        }
    }

    @Test void mutationApprovalLifecycleOverHttp() throws Exception {
        var env = new Env();
        try (var server = start(env)) {
            var created = send(request(server, "/foundgine/mutations/approvals").header("Content-Type", "application/json")
                    .POST(HttpRequest.BodyPublishers.ofString(FoundgineGatewayHttpHandlersTest.CREATE)).build());
            assertEquals(201, created.statusCode(), created.body());
            var handle = (String) FoundgineGatewayHttpHandlersTest.json(created.body()).get("handle");
            assertNotNull(handle);

            var executed = send(request(server, "/foundgine/mutations/approvals/" + handle + "/execute")
                    .POST(HttpRequest.BodyPublishers.noBody()).build());
            assertEquals(200, executed.statusCode(), executed.body());
            // Single use: a second execution finds nothing.
            assertEquals(404, send(request(server, "/foundgine/mutations/approvals/" + handle + "/execute")
                    .POST(HttpRequest.BodyPublishers.noBody()).build()).statusCode());
            assertEquals(404, send(request(server, "/foundgine/mutations/approvals/" + handle).DELETE().build()).statusCode());
        }
    }

    @Test void oversizedBodiesAreRejectedWhetherDeclaredOrChunked() throws Exception {
        var env = new Env();
        env.limits.maxRequestBytes = 64;
        env.rebuild();
        try (var server = start(env)) {
            var big = "{\"rootEntity\":\"" + "x".repeat(200) + "\"}";
            var declared = send(request(server, "/foundgine/read").POST(HttpRequest.BodyPublishers.ofString(big)).build());
            assertEquals(413, declared.statusCode());
            // No Content-Length (chunked): the handler counts bytes as they arrive.
            var chunked = send(request(server, "/foundgine/read").POST(HttpRequest.BodyPublishers.ofInputStream(
                    () -> new ByteArrayInputStream(big.getBytes(StandardCharsets.UTF_8)))).build());
            assertEquals(413, chunked.statusCode());
        }
    }

    @Test void malformedRequestsNeverReachAHandler() throws Exception {
        var env = new Env();
        try (var server = start(env)) {
            // java.net.http refuses to send an invalid escape, so speak raw HTTP.
            assertEquals(400, rawStatus(server, "/foundgine/events?after=%zz"));   // bad escape in the query string
            assertEquals(400, rawStatus(server, "/foundgine/capabilities/%zz"));   // bad escape in the path: the JDK rejects the target
        }
    }

    @Test void queryValuesArePercentDecodedAndInvalidCursorsAre400() throws Exception {
        var env = new Env();
        try (var server = start(env)) {
            var res = send(request(server, "/foundgine/events?after=ab%63").GET().build());
            assertEquals(400, res.statusCode());
            assertEquals("0", res.headers().firstValue("X-Foundgine-Latest-Sequence").orElse(null));
        }
    }

    @Test void eventStreamDeliversLiveEventsAndReleasesTheSubscriptionWhenTheClientLeaves() throws Exception {
        var env = new Env();
        try (var server = start(env)) {
            var response = CLIENT.send(request(server, "/foundgine/events").header("Last-Event-ID", "0").GET().build(),
                    HttpResponse.BodyHandlers.ofInputStream());
            assertEquals(200, response.statusCode());
            assertEquals("text/event-stream; charset=utf-8", response.headers().firstValue("Content-Type").orElse(null));
            assertEquals("no-cache, no-store", response.headers().firstValue("Cache-Control").orElse(null));

            var reader = new BufferedReader(new InputStreamReader(response.body(), StandardCharsets.UTF_8));
            assertEquals("retry: 3000", reader.readLine());
            await(() -> env.hub.subscriberCount() == 1);

            var attrs = new LinkedHashMap<String, String>();
            attrs.put("operation.kind", "read");
            attrs.put("owner.digest", env.owner("t1", "alice"));
            env.hub.publish(FoundgineGatewayEventType.EXECUTION_STARTED, "execution", "corr", null, attrs);

            String line;
            var seen = new StringBuilder();
            while ((line = reader.readLine()) != null) {
                seen.append(line).append('\n');
                if (line.startsWith("data: ")) break;
            }
            assertTrue(seen.toString().contains("id: 1\nevent: ExecutionStarted\n"), seen.toString());

            // Dropping the connection is noticed by the next heartbeat write, which frees the subscription.
            response.body().close();
            await(() -> env.hub.subscriberCount() == 0);
        }
    }

    @Test void stoppingTheServerEndsOpenEventStreams() throws Exception {
        var env = new Env();
        var server = start(env);
        var response = CLIENT.send(request(server, "/foundgine/events").GET().build(), HttpResponse.BodyHandlers.ofInputStream());
        var reader = new BufferedReader(new InputStreamReader(response.body(), StandardCharsets.UTF_8));
        assertEquals("retry: 3000", reader.readLine());
        await(() -> env.hub.subscriberCount() == 1);

        server.stop(2);

        // The stream terminates (end of stream or a reset) instead of hanging, and the subscription is released.
        try { while (reader.readLine() != null) { /* drain keep-alives */ } } catch (IOException reset) { /* also fine */ }
        assertEquals(0, env.hub.subscriberCount());
    }

    @Test void afterRequestRunsForEveryRequestAndFailuresAreReportedNotLeaked() throws Exception {
        var env = new Env();
        env.execution.onRead = () -> { throw new IllegalStateException("boom: secret detail"); };
        var cleanups = new AtomicInteger();
        var errors = new AtomicInteger();
        try (var server = GatewayJdkHttpServer.builder(env.router).port(0)
                .afterRequest(cleanups::incrementAndGet).onError(e -> errors.incrementAndGet()).build().start()) {
            assertEquals(200, send(request(server, "/foundgine/capabilities").GET().build()).statusCode());
            var failed = send(request(server, "/foundgine/read").POST(HttpRequest.BodyPublishers.ofString(
                    FoundgineGatewayHttpHandlersTest.READ)).build());
            assertEquals(500, failed.statusCode());
            assertFalse(failed.body().contains("secret"), "exception detail must not reach the caller");
            await(() -> cleanups.get() == 2);
        }
    }

    @Test void bindsToLoopbackByDefaultAndRefusesRestartAfterStop() throws Exception {
        var env = new Env();
        var server = start(env);
        assertTrue(server.address().getAddress().isLoopbackAddress());
        server.stop(0);
        assertThrows(IllegalStateException.class, server::start);
        assertThrows(IllegalArgumentException.class, () -> GatewayJdkHttpServer.builder(env.router).port(70000));
    }
}
