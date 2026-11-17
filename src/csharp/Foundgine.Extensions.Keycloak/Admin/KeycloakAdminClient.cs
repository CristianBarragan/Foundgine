using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Foundgine.Extensions.Keycloak.Configuration;
using Foundgine.Extensions.Keycloak.Tokens;
using Foundgine.Extensions.Temporal.Activities;

namespace Foundgine.Extensions.Keycloak.Admin;

public sealed record KeycloakUser(string Id, string Username, bool Enabled, string? TenantId, string? ProvisionedBy);
public sealed record KeycloakGroup(string Id, string Name, string? TenantId, string? ProvisionedBy);
public sealed record KeycloakNewUser(
    string Username, string? Email, string? FirstName, string? LastName, IReadOnlyList<string> RequiredActions);

/// <summary>
/// Thin Keycloak Admin REST client for onboarding. It authenticates as the worker's service account
/// (<see cref="FoundgineKeycloakOptions.ServiceClientId"/>) via <see cref="KeycloakServiceTokenProvider"/>.
/// It performs no authorization of its own: the operations in <see>
///     <cref>KeycloakAdminOperations</cref>
/// </see>
/// decide what may be
/// touched (tenant and provenance checks), so do not hand this client to code that takes ids from untrusted input.
/// </summary>
public sealed class KeycloakAdminClient(
    IHttpClientFactory httpFactory, KeycloakServiceTokenProvider tokens, FoundgineKeycloakOptions options)
{
    public const string ProvisionedByAttribute = "foundgine_provisioned_by";

    private static readonly Regex TenantPattern = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex UsernamePattern = new("^[A-Za-z0-9._@+-]{1,128}$", RegexOptions.Compiled);

    public static bool IsValidTenant(string? tenant) => tenant is not null && TenantPattern.IsMatch(tenant);
    public static bool IsValidUsername(string? username) => username is not null && UsernamePattern.IsMatch(username);

    // ------------------------------------------------------------------ users

    public async Task<KeycloakUser?> FindUserAsync(string username, CancellationToken ct)
    {
        var array = await GetAsync($"users?username={Uri.EscapeDataString(username)}&exact=true", ct).ConfigureAwait(false) as JsonArray;
        var node = array?.OfType<JsonObject>().FirstOrDefault(u =>
            string.Equals((string?)u["username"], username, StringComparison.OrdinalIgnoreCase));
        return node is null ? null : ToUser(node);
    }

    public async Task<KeycloakUser?> GetUserAsync(string id, CancellationToken ct) =>
        await GetAsync($"users/{Uri.EscapeDataString(id)}", ct, allowNotFound: true).ConfigureAwait(false) is JsonObject n ? ToUser(n) : null;

    /// <returns>The new user id, or null when the username is already taken (409).</returns>
    public async Task<string?> CreateUserAsync(KeycloakNewUser user, string tenant, string provisionedBy, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["username"] = user.Username,
            ["enabled"] = true,
            ["emailVerified"] = false,
            ["attributes"] = new JsonObject
            {
                [options.TenantAttribute] = new JsonArray(tenant),
                [ProvisionedByAttribute] = new JsonArray(provisionedBy)
            }
        };
        if (!string.IsNullOrWhiteSpace(user.Email)) body["email"] = user.Email;
        if (!string.IsNullOrWhiteSpace(user.FirstName)) body["firstName"] = user.FirstName;
        if (!string.IsNullOrWhiteSpace(user.LastName)) body["lastName"] = user.LastName;
        if (user.RequiredActions.Count > 0) body["requiredActions"] = new JsonArray(user.RequiredActions.Select(a => (JsonNode)a).ToArray());

        using var response = await SendAsync(HttpMethod.Post, "users", body, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict) return null;
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return IdFromLocation(response);
    }

    public async Task SetUserEnabledAsync(string id, bool enabled, CancellationToken ct)
    {
        // PUT replaces the representation, so read-modify-write to avoid wiping attributes and roles.
        if (await GetAsync($"users/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false) is not JsonObject user)
            throw new FoundgineNonRetryableOperationException("Keycloak user not found.");
        user["enabled"] = enabled;
        using var response = await SendAsync(HttpMethod.Put, $"users/{Uri.EscapeDataString(id)}", user, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task DeleteUserAsync(string id, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"users/{Uri.EscapeDataString(id)}", null, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound) await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task AssignRealmRolesAsync(string userId, IReadOnlyCollection<string> roles, CancellationToken ct)
    {
        if (roles.Count == 0) return;
        var reps = new JsonArray();
        foreach (var role in roles)
        {
            if (await GetAsync($"roles/{Uri.EscapeDataString(role)}", ct, allowNotFound: true).ConfigureAwait(false) is not JsonObject rep)
                throw new FoundgineNonRetryableOperationException($"Realm role '{role}' does not exist.");
            reps.Add(rep);
        }
        using var response = await SendAsync(HttpMethod.Post, $"users/{Uri.EscapeDataString(userId)}/role-mappings/realm", reps, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false); // adding an existing mapping is a no-op
    }

    public async Task AddUserToGroupAsync(string userId, string groupId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Put,
            $"users/{Uri.EscapeDataString(userId)}/groups/{Uri.EscapeDataString(groupId)}", null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    // ----------------------------------------------------------------- groups

    public async Task<KeycloakGroup?> FindGroupAsync(string name, CancellationToken ct)
    {
        var array = await GetAsync($"groups?search={Uri.EscapeDataString(name)}&exact=true", ct).ConfigureAwait(false) as JsonArray;
        var node = array?.OfType<JsonObject>().FirstOrDefault(g => string.Equals((string?)g["name"], name, StringComparison.Ordinal));
        if (node is null) return null;
        // The list endpoint may omit attributes: re-read the group for the authoritative copy.
        var id = (string?)node["id"];
        return id is not null && await GetAsync($"groups/{Uri.EscapeDataString(id)}", ct, allowNotFound: true).ConfigureAwait(false) is JsonObject full
            ? ToGroup(full) : null;
    }

    /// <returns>The new group id, or null when a top-level group with that name exists (409).</returns>
    public async Task<string?> CreateGroupAsync(string name, string tenant, string provisionedBy, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["name"] = name,
            ["attributes"] = new JsonObject
            {
                [options.TenantAttribute] = new JsonArray(tenant),
                [ProvisionedByAttribute] = new JsonArray(provisionedBy)
            }
        };
        using var response = await SendAsync(HttpMethod.Post, "groups", body, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict) return null;
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return IdFromLocation(response);
    }

    public async Task DeleteGroupAsync(string id, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"groups/{Uri.EscapeDataString(id)}", null, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound) await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ plumbing

    internal string AdminBase()
    {
        if (!string.IsNullOrWhiteSpace(options.AdminBaseUrl)) return options.AdminBaseUrl.TrimEnd('/');
        var authority = options.Authority.TrimEnd('/');
        var i = authority.LastIndexOf("/realms/", StringComparison.Ordinal);
        if (i < 0) throw new FoundgineNonRetryableOperationException(
            "Authority must look like https://host/realms/<realm> (or set AdminBaseUrl) to derive the Admin API address.");
        var realm = authority[(i + "/realms/".Length)..];
        if (realm.Length == 0 || realm.Contains('/')) throw new FoundgineNonRetryableOperationException("Cannot derive the realm from Authority.");
        return $"{authority[..i]}/admin/realms/{realm}";
    }

    private KeycloakUser ToUser(JsonObject n) => new(
        (string?)n["id"] ?? throw new InvalidOperationException("Keycloak user without id."),
        (string?)n["username"] ?? "", (bool?)n["enabled"] ?? false,
        Attr(n, options.TenantAttribute), Attr(n, ProvisionedByAttribute));

    private KeycloakGroup ToGroup(JsonObject n) => new(
        (string?)n["id"] ?? throw new InvalidOperationException("Keycloak group without id."),
        (string?)n["name"] ?? "", Attr(n, options.TenantAttribute), Attr(n, ProvisionedByAttribute));

    private static string? Attr(JsonObject n, string name) =>
        n["attributes"] is JsonObject a && a[name] is JsonArray v && v.Count > 0 ? (string?)v[0] : null;

    private static string? IdFromLocation(HttpResponseMessage r)
    {
        var path = r.Headers.Location?.AbsolutePath;
        return string.IsNullOrEmpty(path) ? null : path[(path.LastIndexOf('/') + 1)..];
    }

    private async Task<JsonNode?> GetAsync(string path, CancellationToken ct, bool allowNotFound = false)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonNode.ParseAsync(body, cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceClientId) || string.IsNullOrWhiteSpace(options.ServiceClientSecret))
            throw new FoundgineNonRetryableOperationException(
                "Keycloak admin operations need ServiceClientId and ServiceClientSecret (a service account with user-management roles).");

        var token = await tokens.GetAccessTokenAsync(ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(method, $"{AdminBase()}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return await httpFactory.CreateClient(nameof(KeycloakAdminClient)).SendAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Client mistakes and permission problems are deterministic, so retrying cannot fix them (non-retryable);
    /// 5xx / 408 / 429 surface as <see cref="HttpRequestException"/> so Temporal retries. Bodies are never echoed beyond a short error code.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var code = (int)response.StatusCode;
        if (code is 408 or 429 or >= 500)
            throw new HttpRequestException($"Keycloak Admin API returned {code}.", null, response.StatusCode);

        string detail = "";
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (JsonNode.Parse(text) is JsonObject o && ((string?)o["errorMessage"] ?? (string?)o["error"]) is { } m)
                detail = ": " + (m.Length > 120 ? m[..120] : m);
        }
        catch (JsonException) { }
        throw new FoundgineNonRetryableOperationException(code is 401 or 403
            ? $"Keycloak Admin API refused the worker's service account ({code}); grant it manage-users, query-users, query-groups and view-realm{detail}"
            : $"Keycloak Admin API rejected the request ({code}){detail}");
    }
}
