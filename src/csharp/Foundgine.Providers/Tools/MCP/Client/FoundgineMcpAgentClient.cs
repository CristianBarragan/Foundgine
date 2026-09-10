using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundgine.Core.Semantic.Capabilities;
using Foundgine.Core.Semantic.Intent;

namespace Foundgine.Providers.Tools.MCP.Client;

/// <summary>
/// Minimal provider-neutral MCP client for the Foundgine capability-discovery workflow.
/// The client is intentionally transport-only: it discovers the host-visible capability
/// contract, lets the caller construct a dynamic ReadIntent from that contract, and sends
/// the intent to the canonical Foundgine MCP query tool. It never supplies security context.
/// </summary>
public sealed class FoundgineMcpAgentClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private int _requestId;

    public FoundgineMcpAgentClient(HttpClient httpClient, string endpoint = "/mcp")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("MCP endpoint is required.", nameof(endpoint));
        _endpoint = endpoint;
    }

    /// <summary>Discovers the caller-visible semantic capability contract.</summary>
    public async Task<SemanticCapabilityContract> DiscoverCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await CallToolAsync("foundgine_capabilities", new { }, cancellationToken);
        return DeserializeToolResult<SemanticCapabilityContract>(response);
    }

    /// <summary>
    /// Executes a dynamic read intent. The caller remains responsible for constructing the
    /// intent from discovered capabilities; authentication, tenant and authorization context
    /// are supplied by the MCP host and are never serialized here.
    /// </summary>
    public async Task<JsonElement> ExecuteQueryAsync(
        ReadIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var response = await CallToolAsync(
            "foundgine_query",
            new { intentJson = JsonSerializer.Serialize(intent, JsonOptions) },
            cancellationToken);
        return DeserializeToolResult<JsonElement>(response);
    }

    /// <summary>
    /// Convenience workflow: discover capabilities, construct a dynamic intent from the
    /// discovered contract, then execute it. The builder receives only the discovered
    /// contract and cannot manufacture host security context through this API.
    /// </summary>
    public async Task<JsonElement> DiscoverAndExecuteAsync(
        Func<SemanticCapabilityContract, ReadIntent> intentFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intentFactory);
        var contract = await DiscoverCapabilitiesAsync(cancellationToken);
        var intent = intentFactory(contract) ??
                     throw new InvalidOperationException("The intent factory returned null.");
        return await ExecuteQueryAsync(intent, cancellationToken);
    }

    private async Task<JsonElement> CallToolAsync(
        string toolName,
        object arguments,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _requestId),
            method = "tools/call",
            @params = new { name = toolName, arguments }
        }, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"MCP request failed with {(int)response.StatusCode}: {body}");

        return ExtractJsonPayload(body);
    }

    private static T DeserializeToolResult<T>(JsonElement rpcResponse)
    {
        if (rpcResponse.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("MCP response is not a JSON object.");

        if (rpcResponse.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"MCP tool call failed: {error}");

        if (!rpcResponse.TryGetProperty("result", out var result))
            throw new InvalidOperationException("MCP response does not contain a result.");

        if (result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException($"MCP tool call returned an error: {result}");

        if (!result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("MCP tool result does not contain content.");

        foreach (var item in content.EnumerateArray())
        {
            if (!item.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                continue;

            var value = text.GetString();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            try
            {
                using var nested = JsonDocument.Parse(value);
                if (typeof(T) == typeof(JsonElement))
                    return (T)(object)nested.RootElement.Clone();
                return nested.RootElement.Deserialize<T>(JsonOptions)
                       ?? throw new InvalidOperationException("MCP tool result deserialized to null.");
            }
            catch (JsonException)
            {
                // Continue looking for another textual content block.
            }
        }

        throw new InvalidOperationException("MCP tool result did not contain a JSON payload.");
    }

    private static JsonElement ExtractJsonPayload(string body)
    {
        var dataLines = body
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].TrimStart())
            .Where(line => line.Length > 0)
            .ToArray();

        var json = dataLines.Length > 0 ? string.Join("", dataLines) : body;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("MCP response was not valid JSON or SSE JSON.", ex);
        }
    }
}