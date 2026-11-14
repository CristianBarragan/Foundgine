using Foundgine.Core.Execution;
using Foundgine.Core.Serialization;
using Foundgine.Core.Semantic.Security.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Tools.MCP;

/// <summary>
/// DI registration for the Foundgine MCP adapter. The application remains
/// responsible for configuring the MCP transport and for supplying execution
/// context such as tenant and caller identity.
/// </summary>
public static class FoundgineMcpServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    /// <param name="contextFactory">Supplies execution context values per call.</param>
    /// <param name="securityContextProvider">
    /// Host-owned source of the caller's <see cref="SecurityExecutionContext"/>. Prefer this
    /// over <paramref name="securityContextFactory"/> for new hosts; both may not be supplied
    /// together.
    /// </param>
    /// <param name="securityContextFactory">
    /// Obsolete delegate form of <paramref name="securityContextProvider"/>, retained for
    /// existing hosts.
    /// </param>
    /// <param name="approvalTokenSigner"></param>
    /// <param name="approvalTokenKeys"></param>
    /// <param name="approvalTokenPolicy"></param>
    public static IServiceCollection AddFoundgineMcp(
        this IServiceCollection services,
        Func<ExecutionContext>? contextFactory = null,
        ISecurityExecutionContextProvider? securityContextProvider = null,
        Func<SecurityExecutionContext?>? securityContextFactory = null,
        IFoundgineMcpApprovalTokenSigner? approvalTokenSigner = null,
        IFoundgineMcpApprovalTokenKeyResolver? approvalTokenKeys = null,
        FoundgineMcpApprovalTokenPolicy? approvalTokenPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (securityContextProvider is not null && securityContextFactory is not null)
            throw new ArgumentException(
                $"Only one of {nameof(securityContextProvider)} or {nameof(securityContextFactory)} may be supplied.",
                nameof(securityContextFactory));

        services.AddSingleton<JsonReadIntentAdapter>();
        services.AddSingleton<FoundgineMcpTools>();
        services.AddSingleton<FoundgineMcpMutationTools>(sp => new FoundgineMcpMutationTools(
            sp.GetService<Foundgine.Runtime.IFoundgineMutations>(),
            securityContextFactory,
            sp.GetService<ISecurityExecutionContextProvider>(),
            approvalTokenSigner,
            approvalTokenKeys,
            approvalTokenPolicy));

        if (contextFactory is not null)
            services.AddSingleton(contextFactory);

        if (securityContextProvider is not null)
            services.AddSingleton(securityContextProvider);
        else if (securityContextFactory is not null)
            services.AddSingleton<ISecurityExecutionContextProvider>(
                new DelegateSecurityExecutionContextProvider(securityContextFactory));

        return services;
    }

    /// <summary>
    /// Registers <see cref="FoundgineMcpSessionManager"/> and <see cref="FoundgineMcpSessionGate"/> as singletons. Sessions never
    /// replace request authentication. The default in-memory store is for single-instance hosts; supply a shared
    /// <see cref="IFoundgineMcpSessionStore"/> for clusters.
    /// </summary>
    public static IServiceCollection AddFoundgineMcpSessions(
        this IServiceCollection services,
        FoundgineMcpSessionOptions? options = null,
        IFoundgineMcpSessionStore? store = null,
        IFoundgineMcpAuditSink? auditSink = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var manager = new FoundgineMcpSessionManager(options, store);
        services.AddSingleton(manager);
        services.AddSingleton(new FoundgineMcpSessionGate(manager, auditSink));
        return services;
    }

    /// <summary>
    /// Registers the SSE replay registry, the <see cref="FoundgineMcpSsePublisher"/>, the endpoint options and the
    /// <see cref="FoundgineMcpSseSweeper"/> hosted service. Needs <see cref="AddFoundgineMcpSessions"/>. Map the endpoints with
    /// <c>app.MapFoundgineMcpSse()</c>. A <c>Limits</c> value in the options is shared by all streams; leave it null for the defaults.
    /// </summary>
    public static IServiceCollection AddFoundgineMcpSse(
        this IServiceCollection services,
        FoundgineMcpSseEndpointOptions? options = null,
        int maxStreams = 10_000, int maxEvents = 256, long maxBytes = 1_048_576)
    {
        ArgumentNullException.ThrowIfNull(services);
        var validated = (options ?? new FoundgineMcpSseEndpointOptions()).Validated();
        var registry = new FoundgineMcpSseRegistry(maxStreams, maxEvents, maxBytes);
        if (validated.WriteQueueCapacity < maxEvents)
            throw new ArgumentException("The write queue must hold at least one full replay (maxEvents).", nameof(options));
        services.AddSingleton(validated);
        services.AddSingleton(registry);
        services.AddSingleton(new FoundgineMcpSsePublisher(registry));
        services.AddHostedService<FoundgineMcpSseSweeper>();
        return services;
    }

    /// <summary>
    /// Registers SSE from configuration, mirroring the Java <c>foundgine.mcp.sse-*</c> properties. Nothing is registered unless
    /// <c>Enabled</c> is <c>true</c> (the equivalent of <c>sse-enabled</c>). Keys under <paramref name="section"/> (for example
    /// <c>Foundgine:Mcp:Sse</c>), with the Java names and defaults: <c>Enabled</c> (false), <c>HeartbeatSeconds</c> (25),
    /// <c>MaxStreamSeconds</c> (3600; 0 = no limit), <c>SweepSeconds</c> (60), <c>MaxOpenStreams</c> (5000),
    /// <c>MaxStreamsPerPrincipal</c> (10), <c>MaxStreams</c> (10000, sessions with a buffer), <c>MaxEvents</c> (256),
    /// <c>MaxBytes</c> (1048576) and <c>AllowedOrigins</c> (a list; empty = refuse any request that sends an <c>Origin</c>).
    /// Needs <see cref="AddFoundgineMcpSessions"/>; without it the endpoints answer 405. A value that is not a number throws
    /// <see cref="FormatException"/> at startup rather than being ignored.
    /// </summary>
    public static IServiceCollection AddFoundgineMcpSse(
        this IServiceCollection services, IConfiguration section, IFoundgineMcpAuditSink? audit = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);
        var enabled = section["Enabled"];
        if (enabled is null || !bool.TryParse(enabled, out var on) || !on) return services;
        var origins = section.GetSection("AllowedOrigins").GetChildren().Select(c => c.Value)
            .Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToList();
        var options = new FoundgineMcpSseEndpointOptions(
            AllowedOrigins: origins,
            HeartbeatSeconds: IntOf(section, "HeartbeatSeconds", 25),
            MaxStreamSeconds: IntOf(section, "MaxStreamSeconds", 3600),
            SweepSeconds: IntOf(section, "SweepSeconds", 60),
            Limits: new FoundgineMcpSseLimits(IntOf(section, "MaxOpenStreams", 5_000), IntOf(section, "MaxStreamsPerPrincipal", 10)),
            Audit: audit);
        return services.AddFoundgineMcpSse(options, IntOf(section, "MaxStreams", 10_000), IntOf(section, "MaxEvents", 256),
            long.Parse(section["MaxBytes"] ?? "1048576", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static int IntOf(IConfiguration section, string key, int fallback) =>
        section[key] is { Length: > 0 } v ? int.Parse(v, System.Globalization.CultureInfo.InvariantCulture) : fallback;
}
