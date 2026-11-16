using System.Text.Json;
using Foundgine.Extensions.Keycloak.Configuration;

namespace Foundgine.Extensions.Keycloak.Tokens;

/// <summary>Client-credentials access token for calling other Keycloak-protected services from operations. Cached until near expiry.</summary>
public sealed class KeycloakServiceTokenProvider(
    IHttpClientFactory httpFactory, FoundgineKeycloakOptions options, TimeProvider? time = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private string? _token;
    private DateTimeOffset _expires;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceClientId) || string.IsNullOrWhiteSpace(options.ServiceClientSecret))
            throw new InvalidOperationException("ServiceClientId and ServiceClientSecret must be configured.");

        if (_token is not null && _time.GetUtcNow() < _expires) return _token;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_token is not null && _time.GetUtcNow() < _expires) return _token;

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{options.Authority.TrimEnd('/')}/protocol/openid-connect/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = options.ServiceClientId,
                    ["client_secret"] = options.ServiceClientSecret
                })
            };
            using var response = await httpFactory.CreateClient(nameof(KeycloakServiceTokenProvider))
                .SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode(); // body is never logged: it may contain the token

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            _token = doc.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("Keycloak returned no access_token.");
            var seconds = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 60;
            _expires = _time.GetUtcNow().AddSeconds(Math.Max(5, seconds - 30)); // refresh 30s early
            return _token;
        }
        finally { _gate.Release(); }
    }
}
