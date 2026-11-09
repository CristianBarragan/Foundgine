package com.foundgine.extensions.keycloak;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.extensions.keycloak.admin.KeycloakAdminClient;
import com.foundgine.extensions.keycloak.admin.KeycloakAdminOperations;
import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakOptions;
import com.foundgine.extensions.keycloak.tokens.KeycloakServiceTokenProvider;
import com.foundgine.extensions.temporal.activities.FoundgineNonRetryableOperationException;
import com.foundgine.extensions.temporal.activities.FoundgineOperationContext;
import com.foundgine.providers.json.FoundgineJson;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.http.HttpClient;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;

/** Runs the onboarding operations against an in-process fake of the Keycloak token and Admin endpoints. */
class KeycloakAdminOperationsTest {
    private static final String ADMIN = "/admin/realms/foundgine/";

    private final Map<String, Map<String, Object>> users = new LinkedHashMap<>();
    private final Map<String, Map<String, Object>> groups = new LinkedHashMap<>();
    private final Map<String, List<String>> userRoles = new LinkedHashMap<>();
    private final Map<String, List<String>> membership = new LinkedHashMap<>();
    private volatile int forcedStatus;
    private final HttpServer server;
    private final FoundgineKeycloakOptions options = new FoundgineKeycloakOptions();
    private final KeycloakAdminOperations.CreateTenantGroup createGroup;
    private final KeycloakAdminOperations.DeleteTenantGroup deleteGroup;
    private final KeycloakAdminOperations.CreateUser createUser;
    private final KeycloakAdminOperations.DeleteUser deleteUser;
    private final KeycloakAdminOperations.DisableUser disableUser;

    KeycloakAdminOperationsTest() throws IOException {
        server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.createContext("/", this::handle);
        server.start();
        options.authority = "http://127.0.0.1:" + server.getAddress().getPort() + "/realms/foundgine";
        options.requireHttpsMetadata = false;
        options.serviceClientId = "worker";
        options.serviceClientSecret = "secret";
        options.adminAssignableRoles.add("sales");
        var http = HttpClient.newHttpClient();
        var admin = new KeycloakAdminClient(http, new KeycloakServiceTokenProvider(options), options);
        createGroup = new KeycloakAdminOperations.CreateTenantGroup(admin);
        deleteGroup = new KeycloakAdminOperations.DeleteTenantGroup(admin);
        createUser = new KeycloakAdminOperations.CreateUser(admin, options);
        deleteUser = new KeycloakAdminOperations.DeleteUser(admin);
        disableUser = new KeycloakAdminOperations.DisableUser(admin);
    }

    @AfterEach
    void stop() { server.stop(0); }

    private static FoundgineOperationContext ctx(String tenant, String workflow, String payload) {
        return new FoundgineOperationContext(tenant, "alice", null, payload, Map.of(), workflow + ":1", false, 1);
    }

    private static String idOf(com.foundgine.extensions.temporal.contracts.FoundgineOperationResult r, String key) {
        return (String) ((Map<?, ?>) FoundgineJson.parse(r.outputJson())).get(key);
    }

    // ---------------------------------------------------------------------------------------------- fake server

    private synchronized void handle(HttpExchange ex) throws IOException {
        try {
            var path = ex.getRequestURI().getPath();
            var query = ex.getRequestURI().getRawQuery();
            var method = ex.getRequestMethod();
            var body = new String(ex.getRequestBody().readAllBytes(), StandardCharsets.UTF_8);
            if (path.endsWith("/protocol/openid-connect/token")) { reply(ex, 200, "{\"access_token\":\"tok\",\"expires_in\":300}"); return; }
            if (!"Bearer tok".equals(ex.getRequestHeaders().getFirst("Authorization"))) { reply(ex, 401, "{}"); return; }
            if (forcedStatus != 0) { reply(ex, forcedStatus, "{\"errorMessage\":\"forced\"}"); return; }
            if (!path.startsWith(ADMIN)) { reply(ex, 404, "{}"); return; }
            var rest = path.substring(ADMIN.length());
            var seg = rest.split("/");
            switch (seg[0]) {
                case "users" -> users(ex, method, seg, query, body);
                case "groups" -> groups(ex, method, seg, query, body);
                case "roles" -> reply(ex, "sales".equals(seg[1]) || "admin".equals(seg[1]) ? 200 : 404,
                        "{\"id\":\"r-" + seg[1] + "\",\"name\":\"" + seg[1] + "\"}");
                default -> reply(ex, 404, "{}");
            }
        } catch (RuntimeException e) {
            reply(ex, 500, "{}");
        }
    }

    @SuppressWarnings("unchecked")
    private void users(HttpExchange ex, String method, String[] seg, String query, String body) throws IOException {
        if (seg.length == 1 && method.equals("GET")) {
            var name = java.net.URLDecoder.decode(query.split("&")[0].substring("username=".length()), StandardCharsets.UTF_8);
            var out = new ArrayList<Object>();
            users.values().forEach(u -> { if (name.equalsIgnoreCase((String) u.get("username"))) out.add(u); });
            reply(ex, 200, json(out));
        } else if (seg.length == 1 && method.equals("POST")) {
            var user = new LinkedHashMap<String, Object>((Map<String, Object>) FoundgineJson.parse(body));
            for (var u : users.values()) if (user.get("username").equals(u.get("username"))) { reply(ex, 409, "{}"); return; }
            var id = UUID.randomUUID().toString();
            user.put("id", id);
            users.put(id, user);
            ex.getResponseHeaders().add("Location", "http://127.0.0.1/admin/realms/foundgine/users/" + id);
            reply(ex, 201, "");
        } else if (seg.length == 2 && users.containsKey(seg[1])) {
            switch (method) {
                case "GET" -> reply(ex, 200, json(users.get(seg[1])));
                case "PUT" -> { users.put(seg[1], (Map<String, Object>) FoundgineJson.parse(body)); reply(ex, 204, ""); }
                case "DELETE" -> { users.remove(seg[1]); reply(ex, 204, ""); }
                default -> reply(ex, 405, "{}");
            }
        } else if (seg.length == 4 && seg[2].equals("role-mappings")) {
            var names = userRoles.computeIfAbsent(seg[1], k -> new ArrayList<>());
            for (var r : (List<Object>) FoundgineJson.parse(body)) names.add((String) ((Map<?, ?>) r).get("name"));
            reply(ex, 204, "");
        } else if (seg.length == 4 && seg[2].equals("groups")) {
            membership.computeIfAbsent(seg[1], k -> new ArrayList<>()).add(seg[3]);
            reply(ex, 204, "");
        } else reply(ex, 404, "{}");
    }

    @SuppressWarnings("unchecked")
    private void groups(HttpExchange ex, String method, String[] seg, String query, String body) throws IOException {
        if (seg.length == 1 && method.equals("GET")) {
            var name = java.net.URLDecoder.decode(query.split("&")[0].substring("search=".length()), StandardCharsets.UTF_8);
            var out = new ArrayList<Object>();
            groups.values().forEach(g -> { if (name.equals(g.get("name"))) out.add(Map.of("id", g.get("id"), "name", g.get("name"))); });
            reply(ex, 200, json(out));
        } else if (seg.length == 1 && method.equals("POST")) {
            var group = new LinkedHashMap<String, Object>((Map<String, Object>) FoundgineJson.parse(body));
            for (var g : groups.values()) if (group.get("name").equals(g.get("name"))) { reply(ex, 409, "{}"); return; }
            var id = UUID.randomUUID().toString();
            group.put("id", id);
            groups.put(id, group);
            ex.getResponseHeaders().add("Location", "http://127.0.0.1/admin/realms/foundgine/groups/" + id);
            reply(ex, 201, "");
        } else if (seg.length == 2 && groups.containsKey(seg[1])) {
            if (method.equals("GET")) reply(ex, 200, json(groups.get(seg[1])));
            else { groups.remove(seg[1]); reply(ex, 204, ""); }
        } else reply(ex, 404, "{}");
    }

    private static String json(Object value) {
        var sb = new StringBuilder();
        write(sb, value);
        return sb.toString();
    }

    private static void write(StringBuilder sb, Object v) {
        if (v == null) sb.append("null");
        else if (v instanceof String s) FoundgineJson.writeString(sb, s);
        else if (v instanceof Boolean || v instanceof Number) sb.append(v);
        else if (v instanceof Map<?, ?> m) {
            sb.append('{');
            boolean first = true;
            for (var e : m.entrySet()) {
                if (!first) sb.append(',');
                first = false;
                FoundgineJson.writeString(sb, String.valueOf(e.getKey()));
                sb.append(':');
                write(sb, e.getValue());
            }
            sb.append('}');
        } else {
            sb.append('[');
            boolean first = true;
            for (var e : (List<?>) v) {
                if (!first) sb.append(',');
                first = false;
                write(sb, e);
            }
            sb.append(']');
        }
    }

    private static void reply(HttpExchange ex, int status, String body) throws IOException {
        var bytes = body.getBytes(StandardCharsets.UTF_8);
        ex.getResponseHeaders().add("Content-Type", "application/json");
        ex.sendResponseHeaders(status, bytes.length == 0 ? -1 : bytes.length);
        if (bytes.length > 0) ex.getResponseBody().write(bytes);
        ex.close();
    }

    // ------------------------------------------------------------------------------------------------- tests

    @Test
    void onboardingCreatesGroupAndUserWithRolesAndMembership() throws Exception {
        var groupId = idOf(createGroup.execute(ctx("acme", "wf-1", "{}"), CancellationToken.NONE), "groupId");
        var userId = idOf(createUser.execute(ctx("acme", "wf-1",
                "{\"username\":\"bob\",\"email\":\"bob@acme.test\",\"roles\":[\"sales\"],\"requiredActions\":[\"UPDATE_PASSWORD\"]}"),
                CancellationToken.NONE), "userId");
        assertNotNull(userId);
        assertEquals(List.of("sales"), userRoles.get(userId));
        assertEquals(List.of(groupId), membership.get(userId));
        var stored = users.get(userId);
        assertEquals(List.of("acme"), ((Map<?, ?>) stored.get("attributes")).get("tenant_id"));
        assertEquals(List.of("wf-1"), ((Map<?, ?>) stored.get("attributes")).get(KeycloakAdminClient.PROVISIONED_BY_ATTRIBUTE));
        assertEquals(Boolean.TRUE, stored.get("enabled"));
    }

    @Test
    void stepsAreIdempotentForTheSameWorkflowAndRefuseOthers() throws Exception {
        var g1 = idOf(createGroup.execute(ctx("acme", "wf-1", "{}"), CancellationToken.NONE), "groupId");
        assertEquals(g1, idOf(createGroup.execute(ctx("acme", "wf-1", "{}"), CancellationToken.NONE), "groupId")); // retry
        assertThrows(FoundgineNonRetryableOperationException.class, () -> createGroup.execute(ctx("acme", "wf-2", "{}"), CancellationToken.NONE));

        var payload = "{\"username\":\"bob\"}";
        var u1 = idOf(createUser.execute(ctx("acme", "wf-1", payload), CancellationToken.NONE), "userId");
        assertEquals(u1, idOf(createUser.execute(ctx("acme", "wf-1", payload), CancellationToken.NONE), "userId"));
        var taken = assertThrows(FoundgineNonRetryableOperationException.class,
                () -> createUser.execute(ctx("other", "wf-9", payload), CancellationToken.NONE));
        assertEquals("That username is not available.", taken.getMessage());
    }

    @Test
    void compensationOnlyRemovesWhatTheWorkflowCreated() throws Exception {
        createGroup.execute(ctx("acme", "wf-1", "{}"), CancellationToken.NONE);
        createUser.execute(ctx("acme", "wf-1", "{\"username\":\"bob\"}"), CancellationToken.NONE);
        deleteUser.execute(ctx("acme", "wf-2", "{\"username\":\"bob\"}"), CancellationToken.NONE); // other workflow: untouched
        deleteGroup.execute(ctx("acme", "wf-2", "{}"), CancellationToken.NONE);
        assertEquals(1, users.size());
        assertEquals(1, groups.size());
        deleteUser.execute(ctx("acme", "wf-1", "{\"username\":\"bob\"}"), CancellationToken.NONE);
        deleteGroup.execute(ctx("acme", "wf-1", "{}"), CancellationToken.NONE);
        deleteGroup.execute(ctx("acme", "wf-1", "{}"), CancellationToken.NONE); // already gone: not an error
        assertTrue(users.isEmpty());
        assertTrue(groups.isEmpty());
    }

    @Test
    void payloadsAreValidatedAgainstAllowListsAndShape() {
        assertThrows(FoundgineNonRetryableOperationException.class, () -> createUser.execute(
                ctx("acme", "wf-1", "{\"username\":\"bob\",\"roles\":[\"admin\"]}"), CancellationToken.NONE)); // not assignable
        assertThrows(FoundgineNonRetryableOperationException.class, () -> createUser.execute(
                ctx("acme", "wf-1", "{\"username\":\"bob\",\"requiredActions\":[\"TERMS_AND_CONDITIONS\"]}"), CancellationToken.NONE));
        assertThrows(FoundgineNonRetryableOperationException.class, () -> createUser.execute(
                ctx("acme", "wf-1", "{\"username\":\"bad name\"}"), CancellationToken.NONE));
        assertThrows(FoundgineNonRetryableOperationException.class, () -> createUser.execute(
                ctx("acme", "wf-1", "{\"username\":\"bob\",\"roles\":\"sales\"}"), CancellationToken.NONE));
        assertThrows(FoundgineNonRetryableOperationException.class, () -> createUser.execute(
                ctx("acme", "wf-1", "not json"), CancellationToken.NONE));
        assertThrows(FoundgineNonRetryableOperationException.class, () -> createUser.execute(
                ctx("a/b", "wf-1", "{\"username\":\"bob\"}"), CancellationToken.NONE)); // tenant not valid for Keycloak
        assertThrows(FoundgineNonRetryableOperationException.class, () -> createUser.execute(
                new FoundgineOperationContext("acme", "alice", null, "{\"username\":\"bob\"}", Map.of(), "nocolon", false, 1),
                CancellationToken.NONE));
        assertTrue(users.isEmpty());
    }

    @Test
    void disableUserKeepsAttributesAndStaysInsideTheTenant() throws Exception {
        var id = idOf(createUser.execute(ctx("acme", "wf-1", "{\"username\":\"bob\"}"), CancellationToken.NONE), "userId");
        assertThrows(FoundgineNonRetryableOperationException.class,
                () -> disableUser.execute(ctx("other", "wf-2", "{\"username\":\"bob\"}"), CancellationToken.NONE));
        assertEquals(id, idOf(disableUser.execute(ctx("acme", "wf-2", "{\"username\":\"bob\"}"), CancellationToken.NONE), "userId"));
        assertEquals(Boolean.FALSE, users.get(id).get("enabled"));
        assertNotNull(users.get(id).get("attributes")); // read-modify-write kept the attributes
        assertThrows(FoundgineNonRetryableOperationException.class,
                () -> disableUser.execute(ctx("acme", "wf-2", "{\"username\":\"ghost\"}"), CancellationToken.NONE));
    }

    @Test
    void serverErrorsAreRetryableAndClientErrorsAreNot() {
        forcedStatus = 503;
        assertThrows(IOException.class, () -> createGroup.execute(ctx("acme", "wf-1", "{}"), CancellationToken.NONE));
        forcedStatus = 403;
        var denied = assertThrows(FoundgineNonRetryableOperationException.class,
                () -> createGroup.execute(ctx("acme", "wf-1", "{}"), CancellationToken.NONE));
        assertTrue(denied.getMessage().contains("refused the worker's service account"));
        assertFalse(denied.getMessage().contains("secret"));
    }

    @Test
    void adminBaseIsDerivedFromTheAuthorityAndNeedsServiceCredentials() throws Exception {
        options.serviceClientSecret = null;
        var noSecret = assertThrows(FoundgineNonRetryableOperationException.class,
                () -> new KeycloakAdminOperations.CreateTenantGroup(new KeycloakAdminClient(HttpClient.newHttpClient(),
                        new KeycloakServiceTokenProvider(options), options)).execute(ctx("acme", "wf-1", "{}"), CancellationToken.NONE));
        assertTrue(noSecret.getMessage().contains("ServiceClientId"));
        assertTrue(KeycloakAdminClient.isValidTenant("acme-1.x"));
        assertFalse(KeycloakAdminClient.isValidTenant("a b"));
        assertFalse(KeycloakAdminClient.isValidTenant(null));
        assertNull(users.get("none"));
    }
}
