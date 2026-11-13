namespace Foundgine.Providers.Tools.MCP;

/// <summary>
/// The HTTP decisions of the MCP SSE endpoint, free of ASP.NET: which status a <c>GET</c> or <c>DELETE</c> gets, and the live
/// stream when a <c>GET</c> is accepted. The same behaviour as the Java <c>FoundgineMcpSseTransport</c>; the ASP.NET endpoint is a
/// thin adapter over it, and both runtimes run <c>mcp-sse-transport-scenarios.json</c> against it.
/// </summary>
/// <remarks>
/// Order of checks: origin (403), then the session gate (400 missing id, 404 not acceptable), then opening the stream (400 invalid
/// <c>Last-Event-ID</c>, 409 a stream is already open, 410 id no longer retained, 429 a limit is reached, 404 the session ended
/// meanwhile). <c>DELETE</c>: origin (403), gate (400/404), then 204 and the buffer is retired.
/// </remarks>
public static class FoundgineMcpSseTransport
{
    /// <param name="AllowedOrigins">Origins accepted in <c>Origin</c>; a request without one is accepted.</param>
    /// <param name="MaxStreamSeconds">A stream older than this is ended at its next heartbeat; 0 = no limit.</param>
    /// <param name="Limits">Open-stream caps; null = unlimited.</param>
    /// <param name="Audit">Receives SseOpened / SseRejected / SseClosed.</param>
    public sealed record Settings(IReadOnlyList<string>? AllowedOrigins, int MaxStreamSeconds, FoundgineMcpSseLimits? Limits,
        IFoundgineMcpAuditSink? Audit);

    /// <param name="Status">The HTTP status.</param>
    /// <param name="Stream">Non-null only when <paramref name="Status"/> is 200; the caller must <see cref="FoundgineMcpSseStream.Start"/> it.</param>
    public sealed record GetResult(int Status, FoundgineMcpSseStream? Stream);

    public static bool OriginRefused(IReadOnlyList<string>? allowedOrigins, string? origin) =>
        !string.IsNullOrEmpty(origin) && !(allowedOrigins?.Contains(origin, StringComparer.Ordinal) ?? false);

    public static GetResult Get(FoundgineMcpSessionGate gate, FoundgineMcpSseRegistry registry, Settings settings, string? origin,
        string? sessionId, string? owner, string? lastEventId, IFoundgineMcpSseSink sink, Action<Action> executor)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        if (OriginRefused(settings.AllowedOrigins, origin)) return new GetResult(403, null);
        var outcome = gate.AdmitGet(sessionId, owner);
        if (!outcome.Proceeds) return new GetResult(outcome.HttpStatus, null);
        var options = new FoundgineMcpSseStreamOptions(sessionId, owner, settings.Limits, settings.MaxStreamSeconds * 1000L, settings.Audit, null);
        var opened = FoundgineMcpSseStream.Open(registry.Stream(sessionId!), lastEventId, sink, executor, options);
        return opened.Status switch
        {
            FoundgineMcpSseOpenStatus.Ok => new GetResult(200, opened.Stream),
            FoundgineMcpSseOpenStatus.Invalid => new GetResult(400, null),
            FoundgineMcpSseOpenStatus.Busy => new GetResult(409, null),
            FoundgineMcpSseOpenStatus.Evicted => new GetResult(410, null),
            FoundgineMcpSseOpenStatus.Limit => new GetResult(429, null),
            _ => new GetResult(404, null),   // Gone
        };
    }

    /// <summary>Terminates the caller's session and, on success, retires its buffer (which closes the attached stream). Returns the status.</summary>
    public static int Delete(FoundgineMcpSessionGate gate, FoundgineMcpSseRegistry? registry, Settings settings, string? origin,
        string? sessionId, string? owner)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(settings);
        if (OriginRefused(settings.AllowedOrigins, origin)) return 403;
        var outcome = gate.AdmitDelete(sessionId, owner);
        if (outcome.Proceeds && registry is not null && sessionId is not null) registry.Remove(sessionId);
        return outcome.HttpStatus;
    }
}
