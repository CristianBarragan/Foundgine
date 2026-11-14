using System.Text.Encodings.Web;
using System.Text.Json;

namespace Foundgine.Providers.Tools.MCP;

/// <summary>
/// How trusted host code sends a server-to-client JSON-RPC message (notification, server request, or a response) down a
/// session's SSE stream. The message is validated, re-serialised to one compact line and appended to the session's replay
/// buffer; an open stream is nudged and delivers it, and a client that is not connected receives it on resume.
/// </summary>
/// <remarks>
/// Only sessions that already have a buffer receive anything: the transport creates the buffer when the session is issued, and
/// removes it when the session ends, so publishing to an unknown, ended or evicted session is a no-op (null) and can never
/// allocate memory for an arbitrary id. The publisher does not authenticate; the caller is host code that already knows which
/// session it means.
/// </remarks>
public sealed class FoundgineMcpSsePublisher
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly FoundgineMcpSseRegistry _registry;

    public FoundgineMcpSsePublisher(FoundgineMcpSseRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <returns>The appended event, or null when the session has no stream.</returns>
    /// <exception cref="ArgumentException">The message is not a single JSON-RPC 2.0 object (batches are refused) or is larger than the buffer's byte limit.</exception>
    public FoundgineMcpSseEvent? Publish(string sessionId, string message)
    {
        var compact = Validate(message);
        var buffer = _registry.Find(sessionId);
        return buffer?.Append(compact);
    }

    private static string Validate(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(message); }
        catch (JsonException) { throw new ArgumentException("The message is not valid JSON.", nameof(message)); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("The message must be a single JSON-RPC object.", nameof(message));
            if (!root.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0")
                throw new ArgumentException("The message is not JSON-RPC 2.0.", nameof(message));
            var call = root.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String;
            var response = root.TryGetProperty("id", out _) && (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _));
            if (!call && !response)
                throw new ArgumentException("The message is neither a call nor a response.", nameof(message));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, WriterOptions)) root.WriteTo(writer);
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
