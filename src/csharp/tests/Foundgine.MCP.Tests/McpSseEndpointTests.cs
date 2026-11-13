using System.Security.Claims;
using System.Text;
using Foundgine.Providers.Tools.MCP;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpSseEndpointTests
{
    private sealed class Log : IFoundgineMcpAuditSink
    {
        public readonly List<FoundgineMcpAuditEvent> Events = new();
        public void Record(FoundgineMcpAuditEvent auditEvent) { lock (Events) Events.Add(auditEvent); }
    }

    private sealed record Rig(FoundgineMcpSessionGate Gate, FoundgineMcpSseRegistry Registry, FoundgineMcpSseLimits Limits, Log Log,
        FoundgineMcpSseEndpoints.Services Services);

    private static Rig NewRig(int maxEvents = 256, int perPrincipal = 10, long[]? clock = null, FoundgineMcpSessionOptions? sessions = null)
    {
        var log = new Log();
        var limits = new FoundgineMcpSseLimits(100, perPrincipal);
        var manager = clock is null
            ? new FoundgineMcpSessionManager()
            : new FoundgineMcpSessionManager(sessions, new InMemoryFoundgineMcpSessionStore(), () => clock[0]);
        var gate = new FoundgineMcpSessionGate(manager);
        var registry = new FoundgineMcpSseRegistry(100, maxEvents, 100_000);
        var options = new FoundgineMcpSseEndpointOptions(new[] { "https://app.example" }, 25, 3600, 60, limits, log, 1024).Validated();
        return new Rig(gate, registry, limits, log, new FoundgineMcpSseEndpoints.Services(gate, registry, options));
    }

    private static string Issue(Rig rig, string owner = "alice")
    {
        var id = rig.Gate.AdmitPost("initialize", null, owner).IssuedSessionId!;
        rig.Registry.Stream(id);
        return id;
    }

    private static DefaultHttpContext Ctx(string? session, string? user, string? lastEventId = null, string? origin = null, CancellationToken abort = default)
    {
        var c = new DefaultHttpContext();
        if (session is not null) c.Request.Headers[FoundgineMcpSessionManager.HeaderName] = session;
        if (lastEventId is not null) c.Request.Headers["Last-Event-ID"] = lastEventId;
        if (origin is not null) c.Request.Headers.Origin = origin;
        if (user is not null) c.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, user) }, "test"));
        c.Response.Body = new MemoryStream();
        c.RequestAborted = abort;
        return c;
    }

    private static string Body(HttpContext c) => Encoding.UTF8.GetString(((MemoryStream)c.Response.Body).ToArray());

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 500; i++)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    [Fact]
    public async Task GetMapsEveryRefusalToItsStatus()
    {
        var rig = NewRig(maxEvents: 2);
        var id = Issue(rig);
        foreach (var d in new[] { "a", "b", "c" }) rig.Registry.Find(id)!.Append(d);   // keeps 2: ids 2 and 3

        async Task<int> Get(DefaultHttpContext c) { await FoundgineMcpSseEndpoints.HandleGetAsync(c, rig.Services); return c.Response.StatusCode; }
        Assert.Equal(403, await Get(Ctx(id, "alice", origin: "https://evil.example")));
        Assert.Equal(400, await Get(Ctx(null, "alice")));
        Assert.Equal(404, await Get(Ctx(id, "bob")));
        Assert.Equal(404, await Get(Ctx(new string('B', 43), "alice")));
        Assert.Equal(410, await Get(Ctx(id, "alice", lastEventId: "0")));
        Assert.Equal(400, await Get(Ctx(id, "alice", lastEventId: "99")));
        Assert.Equal(400, await Get(Ctx(id, "alice", lastEventId: "abc")));
        Assert.Equal(0, rig.Limits.Open);   // every refusal released its permit and slot
    }

    [Fact]
    public async Task GetStreamsReplayThenLiveEventsAndEndsWhenTheClientGoes()
    {
        var rig = NewRig();
        var id = Issue(rig);
        var buffer = rig.Registry.Find(id)!;
        buffer.Append("a");
        using var abort = new CancellationTokenSource();
        var c = Ctx(id, "alice", lastEventId: "0", origin: "https://app.example", abort: abort.Token);
        var run = FoundgineMcpSseEndpoints.HandleGetAsync(c, rig.Services);

        // the slot is claimed before the handler first yields, so a second GET is refused while this one is open
        var second = Ctx(id, "alice");
        await FoundgineMcpSseEndpoints.HandleGetAsync(second, rig.Services);
        Assert.Equal(409, second.Response.StatusCode);

        buffer.Append("b");
        Assert.True(await Eventually(() => Body(c).Contains("id: 2\ndata: b\n\n")));
        Assert.StartsWith("id: 1\ndata: a\n\n", Body(c));
        Assert.Equal("text/event-stream", c.Response.ContentType);

        abort.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, rig.Limits.Open);
        Assert.Contains(rig.Log.Events, e => e.Type == FoundgineMcpAuditEventType.SseOpened && e.Detail == "resume=true");
        Assert.Contains(rig.Log.Events, e => e.Type == FoundgineMcpAuditEventType.SseClosed && e.Detail == "reason=client_closed");
        Assert.Contains(rig.Log.Events, e => e.Type == FoundgineMcpAuditEventType.SseRejected && e.Detail == "status=BUSY");
    }

    [Fact]
    public async Task PerPrincipalLimitAnswers429()
    {
        var rig = NewRig(perPrincipal: 1);
        var first = Issue(rig);
        var second = Issue(rig);
        using var abort = new CancellationTokenSource();
        var open = FoundgineMcpSseEndpoints.HandleGetAsync(Ctx(first, "alice", abort: abort.Token), rig.Services);
        var refused = Ctx(second, "alice");
        await FoundgineMcpSseEndpoints.HandleGetAsync(refused, rig.Services);
        Assert.Equal(429, refused.Response.StatusCode);
        Assert.Contains(rig.Log.Events, e => e.Type == FoundgineMcpAuditEventType.SseRejected && e.Detail == "status=LIMIT");
        abort.Cancel();
        await open.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DeleteTerminatesTheSessionAndClosesItsStream()
    {
        var rig = NewRig();
        var id = Issue(rig);
        var run = FoundgineMcpSseEndpoints.HandleGetAsync(Ctx(id, "alice"), rig.Services);   // no abort token: only DELETE can end it

        var wrongOwner = Ctx(id, "bob");
        await FoundgineMcpSseEndpoints.HandleDeleteAsync(wrongOwner, rig.Services);
        Assert.Equal(404, wrongOwner.Response.StatusCode);
        Assert.False(run.IsCompleted);

        var delete = Ctx(id, "alice");
        await FoundgineMcpSseEndpoints.HandleDeleteAsync(delete, rig.Services);
        Assert.Equal(204, delete.Response.StatusCode);
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(rig.Registry.Find(id));
        Assert.Equal(0, rig.Limits.Open);
        Assert.Contains(rig.Log.Events, e => e.Type == FoundgineMcpAuditEventType.SseClosed && e.Detail == "reason=session_ended");

        var again = Ctx(id, "alice");
        await FoundgineMcpSseEndpoints.HandleGetAsync(again, rig.Services);
        Assert.Equal(404, again.Response.StatusCode);
    }

    [Fact]
    public async Task SweepDropsBuffersOfExpiredSessionsAndClosesTheirStreams()
    {
        var clock = new[] { 1_000L };
        var rig = NewRig(clock: clock, sessions: new FoundgineMcpSessionOptions(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), 10));
        var id = Issue(rig);
        var run = FoundgineMcpSseEndpoints.HandleGetAsync(Ctx(id, "alice"), rig.Services);
        var sweeper = new FoundgineMcpSseSweeper(rig.Gate, rig.Registry, rig.Services.Options);

        sweeper.Sweep();
        Assert.NotNull(rig.Registry.Find(id));   // still live
        Assert.False(run.IsCompleted);
        clock[0] += 500;
        sweeper.Sweep();
        Assert.Null(rig.Registry.Find(id));      // expired: buffer dropped
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, rig.Limits.Open);
    }

    [Fact]
    public void RegistrationWiresRegistryPublisherOptionsAndSweeper()
    {
        var services = new ServiceCollection();
        services.AddFoundgineMcpSessions();
        services.AddFoundgineMcpSse(new FoundgineMcpSseEndpointOptions(SweepSeconds: 5), maxStreams: 5, maxEvents: 8, maxBytes: 1000);
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<FoundgineMcpSseRegistry>());
        Assert.NotNull(provider.GetRequiredService<FoundgineMcpSsePublisher>());
        Assert.NotNull(provider.GetRequiredService<FoundgineMcpSseEndpointOptions>().Limits);
        Assert.Contains(provider.GetServices<IHostedService>(), s => s is FoundgineMcpSseSweeper);
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddFoundgineMcpSse(
            new FoundgineMcpSseEndpointOptions(WriteQueueCapacity: 4), maxEvents: 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FoundgineMcpSseEndpointOptions(HeartbeatSeconds: 0).Validated());
    }

    [Fact]
    public void ConfigurationRegistersSseOnlyWhenEnabledAndReadsTheLimits()
    {
        static IConfiguration Config(params (string, string?)[] pairs) =>
            new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Item1, p.Item2))).Build();

        var off = new ServiceCollection();
        off.AddFoundgineMcpSessions();
        off.AddFoundgineMcpSse(Config(("MaxOpenStreams", "3")));
        using (var p = off.BuildServiceProvider()) Assert.Null(p.GetService<FoundgineMcpSseRegistry>());

        var on = new ServiceCollection();
        on.AddFoundgineMcpSessions();
        on.AddFoundgineMcpSse(Config(("Enabled", "true"), ("MaxOpenStreams", "3"), ("MaxStreamsPerPrincipal", "2"),
            ("MaxStreamSeconds", "0"), ("SweepSeconds", "7"), ("AllowedOrigins:0", "https://a.example")));
        using var provider = on.BuildServiceProvider();
        var options = provider.GetRequiredService<FoundgineMcpSseEndpointOptions>();
        Assert.NotNull(provider.GetRequiredService<FoundgineMcpSseRegistry>());
        Assert.Equal(0, options.MaxStreamSeconds);
        Assert.Equal(7, options.SweepSeconds);
        Assert.Equal(25, options.HeartbeatSeconds);   // default
        Assert.Equal(new[] { "https://a.example" }, options.AllowedOrigins);

        Assert.Throws<FormatException>(() => new ServiceCollection().AddFoundgineMcpSse(Config(("Enabled", "true"), ("SweepSeconds", "soon"))));
    }
}
