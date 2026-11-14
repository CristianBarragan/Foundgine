using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Foundgine.Providers.Tools.MCP;

/// <summary>Tuning of the MCP SSE endpoint; the same settings as the Java controller's <c>SseOptions</c>.</summary>
/// <param name="AllowedOrigins">Origins accepted in the <c>Origin</c> header (a request without one is accepted).</param>
/// <param name="HeartbeatSeconds">How often an idle stream gets a comment line.</param>
/// <param name="MaxStreamSeconds">A stream older than this is ended so the client reconnects with <c>Last-Event-ID</c>; 0 = no limit.</param>
/// <param name="SweepSeconds">How often expired sessions are purged and the buffers of ended sessions dropped.</param>
/// <param name="Limits">Open-stream caps; defaults to 5000 in total and 10 per principal.</param>
/// <param name="Audit">Receives SSE_OPENED / SSE_REJECTED / SSE_CLOSED events.</param>
/// <param name="WriteQueueCapacity">Frames queued for one slow client before it is closed; must hold at least one replay (the buffer's event bound).</param>
public sealed record FoundgineMcpSseEndpointOptions(
    IReadOnlyList<string>? AllowedOrigins = null, int HeartbeatSeconds = 25, int MaxStreamSeconds = 3600, int SweepSeconds = 60,
    FoundgineMcpSseLimits? Limits = null, IFoundgineMcpAuditSink? Audit = null, int WriteQueueCapacity = 1024)
{
    public FoundgineMcpSseEndpointOptions Validated()
    {
        if (HeartbeatSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(HeartbeatSeconds), "Heartbeat seconds must be positive.");
        if (MaxStreamSeconds < 0) throw new ArgumentOutOfRangeException(nameof(MaxStreamSeconds), "Maximum stream seconds cannot be negative (0 = no limit).");
        if (SweepSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(SweepSeconds), "Sweep seconds must be positive.");
        if (WriteQueueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(WriteQueueCapacity), "Write queue capacity must be positive.");
        return this with { Limits = Limits ?? new FoundgineMcpSseLimits(5_000, 10) };
    }
}

/// <summary>
/// The ASP.NET Core side of MCP server-to-client SSE: <c>GET</c> opens the session's live stream (replay, then live) and
/// <c>DELETE</c> terminates the session and closes its stream. It does not handle <c>POST</c>: the host owns that (typically the
/// ModelContextProtocol SDK) and must call <see cref="FoundgineMcpSessionGate.AdmitPost"/> for it, then
/// <see cref="FoundgineMcpSseRegistry.Stream"/> with the issued id so messages published before the first GET are kept.
/// </summary>
/// <remarks>
/// Statuses for GET: 403 origin refused, 405 SSE not configured, 400 missing session id or invalid <c>Last-Event-ID</c>, 404 the
/// session is not acceptable or ended meanwhile, 409 a stream is already open, 410 the id fell out of the retained window
/// (resynchronise), 429 a stream limit is reached. The principal is <c>HttpContext.User.Identity.Name</c>: authenticate before
/// these endpoints (add <c>RequireAuthorization()</c> to the returned builder). UNVERIFIED: written without a .NET SDK.
/// </remarks>
public static class FoundgineMcpSseEndpoints
{
    /// <summary>Maps GET and DELETE at <paramref name="pattern"/>.</summary>
    public static IEndpointConventionBuilder MapFoundgineMcpSse(this IEndpointRouteBuilder endpoints, string pattern = "/mcp")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(pattern);
        group.MapGet("", (HttpContext context) => Resolve(context) is { } services ? HandleGetAsync(context, services) : NotConfigured(context));
        group.MapDelete("", (HttpContext context) => Resolve(context) is { } services ? HandleDeleteAsync(context, services) : NotConfigured(context));
        return group;
    }

    /// <summary>The services one request needs, resolved from the request's container.</summary>
    public sealed record Services(FoundgineMcpSessionGate Gate, FoundgineMcpSseRegistry Registry, FoundgineMcpSseEndpointOptions Options);

    /// <summary>Null when sessions or SSE are not registered: the endpoint then answers 405, as the Java controller does.</summary>
    private static Services? Resolve(HttpContext context)
    {
        var sp = context.RequestServices;
        var gate = sp.GetService<FoundgineMcpSessionGate>();
        var registry = sp.GetService<FoundgineMcpSseRegistry>();
        var options = sp.GetService<FoundgineMcpSseEndpointOptions>();
        return gate is null || registry is null || options is null ? null : new Services(gate, registry, options);
    }

    private static Task NotConfigured(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        return Task.CompletedTask;
    }

    private static FoundgineMcpSseTransport.Settings TransportOf(FoundgineMcpSseEndpointOptions o) =>
        new(o.AllowedOrigins, o.MaxStreamSeconds, o.Limits, o.Audit);

    private static string? Owner(HttpContext context) => context.User?.Identity?.Name;

    /// <summary>Opens the live stream and holds the response open until either side closes it.</summary>
    public static async Task HandleGetAsync(HttpContext context, Services services)
    {
        var options = services.Options;
        string? sessionId = context.Request.Headers[FoundgineMcpSessionManager.HeaderName];
        string? owner = Owner(context);
        string? lastEventId = context.Request.Headers["Last-Event-ID"];
        string? origin = context.Request.Headers.Origin;
        var queue = Channel.CreateBounded<string>(new BoundedChannelOptions(options.WriteQueueCapacity)
        {
            SingleReader = true, FullMode = BoundedChannelFullMode.Wait,
        });
        var opened = FoundgineMcpSseTransport.Get(services.Gate, services.Registry, TransportOf(options), origin, sessionId, owner,
            lastEventId, new QueueSink(queue.Writer), work => _ = Task.Run(work));
        if (opened.Status != StatusCodes.Status200OK) { context.Response.StatusCode = opened.Status; return; }
        var stream = opened.Stream!;

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";   // tell reverse proxies not to buffer the stream
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        Task? heartbeat = null;
        try
        {
            await context.Response.StartAsync(context.RequestAborted);
            stream.Start();
            heartbeat = HeartbeatLoopAsync(stream, options.HeartbeatSeconds, heartbeatStop.Token);
            await foreach (var frame in queue.Reader.ReadAllAsync(context.RequestAborted))
            {
                await context.Response.WriteAsync(frame, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException) { /* the client went away */ }
        catch (IOException) { /* the connection broke while writing */ }
        finally
        {
            heartbeatStop.Cancel();
            stream.Close();   // no-op if it already closed for another reason; frees the permit and the slot
            if (heartbeat is not null) { try { await heartbeat; } catch (OperationCanceledException) { } }
        }
    }

    private static async Task HeartbeatLoopAsync(FoundgineMcpSseStream stream, int seconds, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        while (await timer.WaitForNextTickAsync(stop)) stream.Heartbeat();
    }

    /// <summary>Terminates the caller's session and closes its stream (reason <c>session_ended</c>).</summary>
    public static Task HandleDeleteAsync(HttpContext context, Services services)
    {
        string? origin = context.Request.Headers.Origin;
        string? sessionId = context.Request.Headers[FoundgineMcpSessionManager.HeaderName];
        context.Response.StatusCode = FoundgineMcpSseTransport.Delete(services.Gate, services.Registry, TransportOf(services.Options),
            origin, sessionId, Owner(context));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Frames are already complete SSE text. They are queued for the response loop instead of written directly, so the sink
    /// never blocks a publisher; a client whose queue is full is too slow and the stream closes (reason <c>write_failed</c>).
    /// </summary>
    private sealed class QueueSink : IFoundgineMcpSseSink
    {
        private readonly ChannelWriter<string> _writer;
        public QueueSink(ChannelWriter<string> writer) => _writer = writer;
        public void Write(string frame)
        {
            if (!_writer.TryWrite(frame)) throw new IOException("The client is too slow or gone.");
        }
        public void Complete() => _writer.TryComplete();
    }
}

/// <summary>
/// Periodically purges expired sessions and drops the SSE buffers of sessions that are no longer live, which closes their
/// streams. The session store is keyed by digest, so only the liveness check can tell which buffers are orphaned.
/// </summary>
public sealed class FoundgineMcpSseSweeper : BackgroundService
{
    private readonly FoundgineMcpSessionGate _gate;
    private readonly FoundgineMcpSseRegistry _registry;
    private readonly TimeSpan _interval;

    public FoundgineMcpSseSweeper(FoundgineMcpSessionGate gate, FoundgineMcpSseRegistry registry, FoundgineMcpSseEndpointOptions options)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _interval = TimeSpan.FromSeconds((options ?? throw new ArgumentNullException(nameof(options))).SweepSeconds);
    }

    /// <summary>One sweep; never throws. Hosts may also call it directly.</summary>
    public void Sweep()
    {
        try
        {
            _gate.PurgeExpired();
            _registry.Sweep(_gate.IsLive);
        }
        catch (Exception) { /* the next run retries; a failing store must not stop the timer */ }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try { while (await timer.WaitForNextTickAsync(stoppingToken)) Sweep(); }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
