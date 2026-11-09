package com.foundgine.extensions.keycloak.admin;

import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakOptions;
import com.foundgine.extensions.keycloak.tokens.KeycloakServiceTokenProvider;
import com.foundgine.extensions.temporal.activities.FoundgineNonRetryableOperationException;
import com.foundgine.providers.json.FoundgineJson;
import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.regex.Pattern;

/**
 * Thin Keycloak Admin REST client for onboarding. It authenticates as the worker's service account
 * ({@link FoundgineKeycloakOptions#serviceClientId}) via {@link KeycloakServiceTokenProvider}. It performs no authorization
 * of its own: the operations in {@link KeycloakAdminOperations} decide what may be touched (tenant and provenance checks),
 * so do not hand this client to code that takes ids from untrusted input.
 */
public final class KeycloakAdminClient {
    public static final String PROVISIONED_BY_ATTRIBUTE = "foundgine_provisioned_by";

    private static final Pattern TENANT = Pattern.compile("^[A-Za-z0-9._-]{1,64}$");
    private static final Pattern USERNAME = Pattern.compile("^[A-Za-z0-9._@+-]{1,128}$");

    private final HttpClient http;
    private final KeycloakServiceTokenProvider tokens;
    private final FoundgineKeycloakOptions options;

    public KeycloakAdminClient(HttpClient http, KeycloakServiceTokenProvider tokens, FoundgineKeycloakOptions options) {
        this.http = Objects.requireNonNull(http, "http");
        this.tokens = Objects.requireNonNull(tokens, "tokens");
        this.options = Objects.requireNonNull(options, "options");
    }

    public static boolean isValidTenant(String tenant) { return tenant != null && TENANT.matcher(tenant).matches(); }

    public static boolean isValidUsername(String username) { return username != null && USERNAME.matcher(username).matches(); }

    // ------------------------------------------------------------------ users

    public KeycloakUser findUser(String username) throws IOException, InterruptedException {
        var array = get("users?username=" + enc(username) + "&exact=true", false);
        if (array instanceof List<?> list)
            for (var item : list)
                if (item instanceof Map<?, ?> u && username.equalsIgnoreCase(str(u.get("username")))) return toUser(u);
        return null;
    }

    public KeycloakUser getUser(String id) throws IOException, InterruptedException {
        return get("users/" + enc(id), true) instanceof Map<?, ?> n ? toUser(n) : null;
    }

    /** @return the new user id, or null when the username is already taken (409) */
    public String createUser(KeycloakNewUser user, String tenant, String provisionedBy) throws IOException, InterruptedException {
        var body = new LinkedHashMap<String, Object>();
        body.put("username", user.username());
        body.put("enabled", true);
        body.put("emailVerified", false);
        body.put("attributes", attributes(tenant, provisionedBy));
        if (hasText(user.email())) body.put("email", user.email());
        if (hasText(user.firstName())) body.put("firstName", user.firstName());
        if (hasText(user.lastName())) body.put("lastName", user.lastName());
        if (!user.requiredActions().isEmpty()) body.put("requiredActions", user.requiredActions());
        var response = send("POST", "users", body);
        if (response.statusCode() == 409) return null;
        ensureSuccess(response);
        return idFromLocation(response);
    }

    public void setUserEnabled(String id, boolean enabled) throws IOException, InterruptedException {
        // PUT replaces the representation, so read-modify-write to avoid wiping attributes and roles.
        if (!(get("users/" + enc(id), false) instanceof Map<?, ?> existing))
            throw new FoundgineNonRetryableOperationException("Keycloak user not found.");
        var user = new LinkedHashMap<String, Object>();
        existing.forEach((k, v) -> user.put(String.valueOf(k), v));
        user.put("enabled", enabled);
        ensureSuccess(send("PUT", "users/" + enc(id), user));
    }

    public void deleteUser(String id) throws IOException, InterruptedException {
        var response = send("DELETE", "users/" + enc(id), null);
        if (response.statusCode() != 404) ensureSuccess(response);
    }

    public void assignRealmRoles(String userId, List<String> roles) throws IOException, InterruptedException {
        if (roles.isEmpty()) return;
        var reps = new ArrayList<Object>();
        for (var role : roles) {
            if (!(get("roles/" + enc(role), true) instanceof Map<?, ?> rep))
                throw new FoundgineNonRetryableOperationException("Realm role '" + role + "' does not exist.");
            reps.add(rep);
        }
        ensureSuccess(send("POST", "users/" + enc(userId) + "/role-mappings/realm", reps)); // existing mapping: no-op
    }

    public void addUserToGroup(String userId, String groupId) throws IOException, InterruptedException {
        ensureSuccess(send("PUT", "users/" + enc(userId) + "/groups/" + enc(groupId), null));
    }

    // ----------------------------------------------------------------- groups

    public KeycloakGroup findGroup(String name) throws IOException, InterruptedException {
        var array = get("groups?search=" + enc(name) + "&exact=true", false);
        if (array instanceof List<?> list)
            for (var item : list)
                if (item instanceof Map<?, ?> g && name.equals(g.get("name"))) {
                    // the list endpoint may omit attributes: re-read the group for the authoritative copy
                    var id = str(g.get("id"));
                    return id != null && get("groups/" + enc(id), true) instanceof Map<?, ?> full ? toGroup(full) : null;
                }
        return null;
    }

    /** @return the new group id, or null when a top-level group with that name exists (409) */
    public String createGroup(String name, String tenant, String provisionedBy) throws IOException, InterruptedException {
        var body = new LinkedHashMap<String, Object>();
        body.put("name", name);
        body.put("attributes", attributes(tenant, provisionedBy));
        var response = send("POST", "groups", body);
        if (response.statusCode() == 409) return null;
        ensureSuccess(response);
        return idFromLocation(response);
    }

    public void deleteGroup(String id) throws IOException, InterruptedException {
        var response = send("DELETE", "groups/" + enc(id), null);
        if (response.statusCode() != 404) ensureSuccess(response);
    }

    // ------------------------------------------------------------------ plumbing

    String adminBase() {
        if (hasText(options.adminBaseUrl)) return options.adminBaseUrl.replaceAll("/+$", "");
        var authority = options.authority.replaceAll("/+$", "");
        int i = authority.lastIndexOf("/realms/");
        if (i < 0) throw new FoundgineNonRetryableOperationException(
                "Authority must look like https://host/realms/<realm> (or set AdminBaseUrl) to derive the Admin API address.");
        var realm = authority.substring(i + "/realms/".length());
        if (realm.isEmpty() || realm.contains("/")) throw new FoundgineNonRetryableOperationException("Cannot derive the realm from Authority.");
        return authority.substring(0, i) + "/admin/realms/" + realm;
    }

    private Map<String, Object> attributes(String tenant, String provisionedBy) {
        var attributes = new LinkedHashMap<String, Object>();
        attributes.put(options.tenantAttribute, List.of(tenant));
        attributes.put(PROVISIONED_BY_ATTRIBUTE, List.of(provisionedBy));
        return attributes;
    }

    private KeycloakUser toUser(Map<?, ?> n) {
        var id = str(n.get("id"));
        if (id == null) throw new IllegalStateException("Keycloak user without id.");
        var username = str(n.get("username"));
        return new KeycloakUser(id, username == null ? "" : username, Boolean.TRUE.equals(n.get("enabled")),
                attr(n, options.tenantAttribute), attr(n, PROVISIONED_BY_ATTRIBUTE));
    }

    private KeycloakGroup toGroup(Map<?, ?> n) {
        var id = str(n.get("id"));
        if (id == null) throw new IllegalStateException("Keycloak group without id.");
        var name = str(n.get("name"));
        return new KeycloakGroup(id, name == null ? "" : name, attr(n, options.tenantAttribute), attr(n, PROVISIONED_BY_ATTRIBUTE));
    }

    private static String attr(Map<?, ?> n, String name) {
        return n.get("attributes") instanceof Map<?, ?> a && a.get(name) instanceof List<?> v && !v.isEmpty() ? str(v.get(0)) : null;
    }

    private static String str(Object o) { return o instanceof String s ? s : null; }

    private static boolean hasText(String s) { return s != null && !s.isBlank(); }

    private static String enc(String s) { return URLEncoder.encode(s, StandardCharsets.UTF_8).replace("+", "%20"); }

    private static String idFromLocation(HttpResponse<String> r) {
        var location = r.headers().firstValue("Location").orElse(null);
        if (location == null || location.isEmpty()) return null;
        var path = URI.create(location).getPath();
        return path == null || path.isEmpty() ? null : path.substring(path.lastIndexOf('/') + 1);
    }

    private Object get(String path, boolean allowNotFound) throws IOException, InterruptedException {
        var response = send("GET", path, null);
        if (allowNotFound && response.statusCode() == 404) return null;
        ensureSuccess(response);
        var text = response.body();
        return text == null || text.isBlank() ? null : FoundgineJson.parse(text);
    }

    private HttpResponse<String> send(String method, String path, Object body) throws IOException, InterruptedException {
        if (!hasText(options.serviceClientId) || !hasText(options.serviceClientSecret))
            throw new FoundgineNonRetryableOperationException(
                    "Keycloak admin operations need ServiceClientId and ServiceClientSecret (a service account with user-management roles).");
        var token = tokens.getAccessToken();
        var publisher = body == null ? HttpRequest.BodyPublishers.noBody()
                : HttpRequest.BodyPublishers.ofString(AdminJson.write(body), StandardCharsets.UTF_8);
        var builder = HttpRequest.newBuilder(URI.create(adminBase() + "/" + path)).timeout(Duration.ofSeconds(30))
                .header("Authorization", "Bearer " + token).method(method, publisher);
        if (body != null) builder.header("Content-Type", "application/json");
        return http.send(builder.build(), HttpResponse.BodyHandlers.ofString());
    }

    /**
     * Client mistakes and permission problems are deterministic, so retrying cannot fix them (non-retryable); 5xx / 408 /
     * 429 surface as {@link IOException} so the activity is retried. Bodies are never echoed beyond a short error code.
     */
    private static void ensureSuccess(HttpResponse<String> response) throws IOException {
        int code = response.statusCode();
        if (code / 100 == 2) return;
        if (code == 408 || code == 429 || code >= 500) throw new IOException("Keycloak Admin API returned " + code + ".");
        var detail = "";
        try {
            if (FoundgineJson.parse(response.body()) instanceof Map<?, ?> o) {
                var m = o.get("errorMessage") instanceof String e ? e : o.get("error") instanceof String e2 ? e2 : null;
                if (m != null) detail = ": " + (m.length() > 120 ? m.substring(0, 120) : m);
            }
        } catch (RuntimeException ignored) {
            // not JSON: no detail
        }
        throw new FoundgineNonRetryableOperationException(code == 401 || code == 403
                ? "Keycloak Admin API refused the worker's service account (" + code
                        + "); grant it manage-users, query-users, query-groups and view-realm" + detail
                : "Keycloak Admin API rejected the request (" + code + ")" + detail);
    }
}
