using System.Security;
using System.Text;
using System.Text.Json;
using Foundgine.SupplyChain.Advanced.Intent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundgine.SupplyChain.Advanced.Intent;

/// <summary>
/// HTTP and MCP surface for the canonical intent boundary (<see cref="SupplyChainIntentService"/>); the C#
/// counterpart of the Java <c>AdvancedIntentEndpoint</c>.
/// <para>
/// There is one request shape. The body of <c>POST /intent</c> is the canonical read intent itself (see
/// <c>JsonReadIntentAdapter</c>), for example:
/// </para>
/// <code>
/// {"rootEntity":"Supplier",
///  "selections":[{"field":"Name"},{"field":"RiskScore"}],
///  "filter":{"kind":"field","field":"Country","operator":"Eq","value":"NZ"}}
/// </code>
/// <list type="bullet">
/// <item><c>POST /intent</c>: execute it as the caller.</item>
/// <item><c>POST /intent/explain</c>: how it resolves, what the caller's policy leaves of it, and the
/// authorized plan; nothing is executed.</item>
/// <item><c>GET /intent/capabilities</c>: what the caller may read, by entity, field, relationship.</item>
/// </list>
/// <para>
/// The caller (tenant and role) comes from the <c>X-Foundgine-Tenant</c> and <c>X-Foundgine-Role</c> headers
/// and never from the body. DEMO ONLY: a real host derives identity from an authenticated principal; see
/// <see cref="IntentCaller"/>.
/// </para>
/// <para>
/// MCP: the <c>foundgine_intent</c> tool takes <c>{"intent": {...}, "mode": "execute|explain|capabilities"}</c>
/// and uses the same service, with the caller taken from the same headers of the <c>/mcp</c> request.
/// </para>
/// </summary>
public static class IntentEndpoint
{
    public const string Path = "/intent";
    public const int MaxBodyBytes = 65_536;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapIntentEndpoints(
        this IEndpointRouteBuilder app,
        SupplyChainIntentService service)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(service);

        app.Map(Path, Route(service, "POST", ExecuteAsync));
        app.Map(Path + "/explain", Route(service, "POST", ExplainAsync));
        app.Map(Path + "/capabilities", Route(service, "GET", CapabilitiesAsync, readsBody: false));
        app.Map(Path + "/{**rest}", http => WriteError(http, 404, "not_found", "Unknown intent path."));
        return app;
    }

    private delegate Task<Dictionary<string, object?>> Operation(
        SupplyChainIntentService service, IntentCaller caller, string body, CancellationToken cancellationToken);

    private static Task<Dictionary<string, object?>> ExecuteAsync(
        SupplyChainIntentService service, IntentCaller caller, string body, CancellationToken cancellationToken) =>
        service.ExecuteAsync(caller, body, cancellationToken);

    private static Task<Dictionary<string, object?>> ExplainAsync(
        SupplyChainIntentService service, IntentCaller caller, string body, CancellationToken cancellationToken) =>
        Task.FromResult(service.Explain(caller, body));

    private static Task<Dictionary<string, object?>> CapabilitiesAsync(
        SupplyChainIntentService service, IntentCaller caller, string body, CancellationToken cancellationToken) =>
        Task.FromResult(service.Capabilities(caller));

    private static RequestDelegate Route(
        SupplyChainIntentService service, string method, Operation operation, bool readsBody = true) =>
        http => Handle(http, service, method, operation, readsBody);

    private static async Task Handle(
        HttpContext http,
        SupplyChainIntentService service,
        string method,
        Operation operation,
        bool readsBody)
    {
        try
        {
            if (!string.Equals(http.Request.Method, method, StringComparison.OrdinalIgnoreCase))
            {
                http.Response.Headers.Allow = method;
                await WriteError(http, 405, "method_not_allowed", $"Use {method} for this path.");
                return;
            }

            var caller = ResolveCaller(http.Request, service);
            var body = readsBody ? await ReadBody(http.Request, http.RequestAborted) : string.Empty;
            await Write(http, 200, await operation(service, caller, body, http.RequestAborted));
        }
        catch (PayloadTooLargeException e)
        {
            await WriteError(http, 413, "payload_too_large", e.Message);
        }
        catch (Exception e)
        {
            var failure = SupplyChainIntentService.Classify(e);
            await WriteError(http, failure.Status, failure.Type, failure.Message);
        }
    }

    /// <summary>The caller for a request, from headers only.</summary>
    public static IntentCaller ResolveCaller(HttpRequest request, SupplyChainIntentService service) =>
        IntentCaller.Resolve(
            request.Headers[IntentCaller.TenantHeader].FirstOrDefault(),
            request.Headers[IntentCaller.RoleHeader].FirstOrDefault(),
            service.KnownTenants);

    private static async Task<string> ReadBody(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxBodyBytes) throw TooLarge();

        // Never trust Content-Length: read at most one byte past the limit.
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) throw TooLarge();
            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static PayloadTooLargeException TooLarge() =>
        new($"An intent may not exceed {MaxBodyBytes} bytes.");

    private sealed class PayloadTooLargeException(string message) : Exception(message);

    private static Task Write(HttpContext http, int status, object body)
    {
        http.Response.StatusCode = status;
        http.Response.ContentType = "application/json";
        return http.Response.Body.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(body, Json)).AsTask();
    }

    private static Task WriteError(HttpContext http, int status, string type, string message) =>
        Write(http, status, new Dictionary<string, object?>
        {
            ["error"] = new Dictionary<string, object?> { ["type"] = type, ["message"] = message }
        });

    // ------------------------------------------------------------------ MCP

    /// <summary>
    /// Handles the <c>foundgine_intent</c> MCP tool and returns the JSON text of the result. Failures are
    /// thrown as the underlying exception; report them with <see cref="ToolFailureMessage"/>.
    /// </summary>
    public static async Task<string> CallToolAsync(
        SupplyChainIntentService service,
        IntentCaller caller,
        JsonElement? intent,
        string? mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(caller);

        object result;
        switch (string.IsNullOrWhiteSpace(mode) ? "execute" : mode)
        {
            case "capabilities":
                result = service.Capabilities(caller);
                break;
            case "explain":
                result = service.Explain(caller, IntentText(intent));
                break;
            case "execute":
                result = await service.ExecuteAsync(caller, IntentText(intent), cancellationToken);
                break;
            default:
                throw new ArgumentException($"Unknown mode '{mode}'. Use execute, explain or capabilities.");
        }

        return JsonSerializer.Serialize(result, Json);
    }

    /// <summary>A tool failure as the MCP client should read it, for example <c>forbidden: Access denied ...</c>.</summary>
    public static string ToolFailureMessage(Exception error)
    {
        var failure = SupplyChainIntentService.Classify(error);
        return $"{failure.Type}: {failure.Message}";
    }

    /// <summary>The intent as a JSON string, from either an object or an already-serialized string.</summary>
    private static string IntentText(JsonElement? intent)
    {
        if (intent is not { } value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new ArgumentException("Missing 'intent'.");

        var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
        if (Encoding.UTF8.GetByteCount(text) > MaxBodyBytes)
            throw new ArgumentException($"An intent may not exceed {MaxBodyBytes} bytes.");
        return text;
    }
}
