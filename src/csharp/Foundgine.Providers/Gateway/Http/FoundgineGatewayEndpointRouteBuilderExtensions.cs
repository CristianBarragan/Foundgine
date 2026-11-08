using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Providers.Gateway.Approvals;
using Foundgine.Providers.Gateway.Events;
using Foundgine.Providers.Gateway.Execution;
using Foundgine.Providers.Gateway.Semantics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
namespace Foundgine.Providers.Gateway.Http;

/// <summary>Maps the transport adapter for the Foundgine gateway onto an ASP.NET Core endpoint route builder.</summary>
public static class FoundgineGatewayEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps capability discovery and Server-Sent Events endpoints.
    /// The handlers use explicit <see cref="RequestDelegate"/> methods and source-generated JSON metadata
    /// to avoid runtime endpoint delegate inference and reflection-based JSON serialization.
    /// </summary>
    /// <param name="endpoints">The host application's endpoint route builder.</param>
    /// <param name="routePrefix">The route prefix. Defaults to <c>/foundgine</c>.</param>
    /// <param name="requireAuthorization"></param>
    public static IEndpointRouteBuilder MapFoundgineGateway(
        this IEndpointRouteBuilder endpoints,
        string routePrefix = "/foundgine",
        bool requireAuthorization = true)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(routePrefix);

        var prefix = routePrefix.TrimEnd('/');
        if (!prefix.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("The gateway route prefix must start with '/'.", nameof(routePrefix));

        var capabilities = endpoints.MapGet($"{prefix}/capabilities", (RequestDelegate)WriteCapabilitiesAsync);
        var capability = endpoints.MapGet($"{prefix}/capabilities/{{id}}", (RequestDelegate)WriteCapabilityAsync);
        var events = endpoints.MapGet($"{prefix}/events", (RequestDelegate)StreamEventsAsync);
        var semantic = endpoints.MapGet($"{prefix}/semantic", (RequestDelegate)WriteSemanticDescriptionAsync);
        if (requireAuthorization)
        {
            capabilities.RequireAuthorization();
            capability.RequireAuthorization();
            events.RequireAuthorization();
            semantic.RequireAuthorization();
        }
        // Execution is always protected, even if a host deliberately makes discovery public.
        // The host must register authentication, authorization and a trusted security context provider.
        endpoints.MapPost($"{prefix}/read", (RequestDelegate)ExecuteReadAsync).RequireAuthorization();
        endpoints.MapPost($"{prefix}/mutations/dry-run", (RequestDelegate)DryRunMutationAsync).RequireAuthorization();
        endpoints.MapPost($"{prefix}/mutations/approvals", (RequestDelegate)CreateMutationApprovalAsync).RequireAuthorization();
        endpoints.MapPost($"{prefix}/mutations/approvals/{{handle}}/execute", (RequestDelegate)ExecuteApprovedMutationAsync).RequireAuthorization();
        endpoints.MapDelete($"{prefix}/mutations/approvals/{{handle}}", (RequestDelegate)RevokeMutationApprovalAsync).RequireAuthorization();
        endpoints.MapGet($"{prefix}/executions/{{id}}", (RequestDelegate)GetExecutionStatusAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task WriteCapabilitiesAsync(HttpContext context)
    {
        var gateway = context.RequestServices.GetRequiredService<IFoundgineGateway>();
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            gateway.Describe(),
            FoundgineGatewayJsonContext.Default.FoundgineGatewayDescription,
            context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteCapabilityAsync(HttpContext context)
    {
        var gateway = context.RequestServices.GetRequiredService<IFoundgineGateway>();
        var id = context.Request.RouteValues.TryGetValue("id", out var routeValue)
            ? Convert.ToString(routeValue, System.Globalization.CultureInfo.InvariantCulture)
            : null;

        if (string.IsNullOrWhiteSpace(id))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var capability = gateway.FindCapability(id);
        if (capability is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            capability,
            FoundgineGatewayJsonContext.Default.FoundgineGatewayCapability,
            context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteSemanticDescriptionAsync(HttpContext context)
    {
        var source = context.RequestServices.GetRequiredService<IFoundgineGatewaySemanticSource>();
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            source.Describe(),
            FoundgineGatewayJsonContext.Default.FoundgineGatewaySemanticDescription,
            context.RequestAborted).ConfigureAwait(false);
    }


    private static async Task ExecuteReadAsync(HttpContext context)
    {
        var limits = context.RequestServices.GetRequiredService<FoundgineGatewayHttpOptions>();
        var maxBodyFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodyFeature is { IsReadOnly: false })
            maxBodyFeature.MaxRequestBodySize = limits.MaxRequestBytes;
        if (context.Request.ContentLength is > 0 && context.Request.ContentLength > limits.MaxRequestBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        FoundgineGatewayReadRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(
                context.Request.Body,
                FoundgineGatewayJsonContext.Default.FoundgineGatewayReadRequest,
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        ReadIntent intent;
        try
        {
            intent = FoundgineGatewayReadRequestMapper.ToIntent(
                request ?? throw new ArgumentException("A JSON request body is required."), limits);
        }
        catch (ArgumentException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var execution = context.RequestServices.GetRequiredService<IFoundgineGatewayExecution>();
        var executionId = Guid.NewGuid().ToString("N");
        await PublishExecutionEventAsync(context, FoundgineGatewayEventType.ExecutionStarted, executionId, "read", null).ConfigureAwait(false);
        ExecutionResult result;
        try
        {
            result = await execution.ExecuteReadAsync(intent, cancellationToken: context.RequestAborted)
                .ConfigureAwait(false);
            await PublishExecutionEventAsync(context, FoundgineGatewayEventType.ExecutionCompleted, executionId, "read", "succeeded").ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            await PublishExecutionEventAsync(context, FoundgineGatewayEventType.ExecutionFailed, executionId, "read", "authorization_denied").ConfigureAwait(false);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            await PublishExecutionEventAsync(context, FoundgineGatewayEventType.ExecutionFailed, executionId, "read", "cancelled").ConfigureAwait(false);
            throw;
        }
        catch
        {
            await PublishExecutionEventAsync(context, FoundgineGatewayEventType.ExecutionFailed, executionId, "read", "failed").ConfigureAwait(false);
            throw;
        }
        context.Response.Headers["X-Foundgine-Execution-Id"] = executionId;

        FoundgineGatewayReadResponse response;
        try
        {
            response = FoundgineGatewayReadResponseMapper.Map(result);
        }
        catch (NotSupportedException)
        {
            // A result type without an explicit AOT wire mapping is never serialized via reflection.
            context.Response.StatusCode = StatusCodes.Status501NotImplemented;
            return;
        }

        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body, response,
            FoundgineGatewayJsonContext.Default.FoundgineGatewayReadResponse,
            context.RequestAborted).ConfigureAwait(false);
    }


    private static async Task DryRunMutationAsync(HttpContext context)
    {
        var graph = await ReadMutationGraphAsync(context).ConfigureAwait(false);
        if (graph is null) return;
        try
        {
            var result = context.RequestServices.GetRequiredService<IFoundgineGatewayExecution>().DryRunMutation(graph);
            var response = new FoundgineGatewayMutationDryRunResponse(result.PlanFingerprint,
                result.Operations.Select(x => new FoundgineGatewayMutationPlanOperation(x.Index, x.Entity, x.Kind, x.Fields, x.ReturnFields)).ToArray(),
                result.Effects);
            await WriteJsonAsync(context, response, FoundgineGatewayJsonContext.Default.FoundgineGatewayMutationDryRunResponse).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException) { context.Response.StatusCode = StatusCodes.Status403Forbidden; }
        catch (ArgumentException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
    }

    private static async Task CreateMutationApprovalAsync(HttpContext context)
    {
        var graph = await ReadMutationGraphAsync(context).ConfigureAwait(false);
        if (graph is null) return;
        try
        {
            var receipt = await context.RequestServices.GetRequiredService<FoundgineGatewayApprovalService>()
                .CreateAsync(graph, context.RequestAborted).ConfigureAwait(false);
            context.Response.StatusCode = StatusCodes.Status201Created;
            await WriteJsonAsync(context, new FoundgineGatewayApprovalResponse(receipt.Handle, receipt.ExpiresAt, receipt.PlanFingerprint),
                FoundgineGatewayJsonContext.Default.FoundgineGatewayApprovalResponse).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException) { context.Response.StatusCode = StatusCodes.Status403Forbidden; }
        catch (ArgumentException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
    }

    private static async Task ExecuteApprovedMutationAsync(HttpContext context)
    {
        var handle = GetHandle(context);
        if (handle is null) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        var executionId = Guid.NewGuid().ToString("N");
        try
        {
            await PublishExecutionEventAsync(context, FoundgineGatewayEventType.ExecutionStarted, executionId, "mutation", null).ConfigureAwait(false);
            var result = await context.RequestServices.GetRequiredService<FoundgineGatewayApprovalService>()
                .ExecuteAsync(handle, context.RequestAborted).ConfigureAwait(false);
            if (result is null)
            {
                await PublishExecutionEventAsync(context, FoundgineGatewayEventType.ExecutionFailed, executionId, "mutation", "approval_unavailable").ConfigureAwait(false);
                context.Response.StatusCode = StatusCodes.Status404NotFound; return;
            }
            await PublishExecutionEventAsync(context, FoundgineGatewayEventType.ExecutionCompleted, executionId, "mutation", "succeeded").ConfigureAwait(false);
            context.Response.Headers["X-Foundgine-Execution-Id"] = executionId;
            var response = new FoundgineGatewayMutationExecutionResponse(result.PlanFingerprint, result.ResultFingerprint,
                result.ApprovalId, result.ApprovedBy, result.Result.Results.Count, result.Result.TotalAffectedRows);
            await WriteJsonAsync(context, response, FoundgineGatewayJsonContext.Default.FoundgineGatewayMutationExecutionResponse).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            await PublishExecutionEventAsync(context, FoundgineGatewayEventType.ExecutionFailed, executionId, "mutation", "authorization_denied").ConfigureAwait(false);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
        }
        catch (ArgumentException)
        {
            await PublishExecutionEventAsync(context, FoundgineGatewayEventType.ExecutionFailed, executionId, "mutation", "invalid_request").ConfigureAwait(false);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            await PublishExecutionEventAsync(context, FoundgineGatewayEventType.ExecutionFailed, executionId, "mutation", "cancelled").ConfigureAwait(false);
            throw;
        }
        catch
        {
            await PublishExecutionEventAsync(context, FoundgineGatewayEventType.ExecutionFailed, executionId, "mutation", "failed").ConfigureAwait(false);
            throw;
        }
    }

    private static async Task RevokeMutationApprovalAsync(HttpContext context)
    {
        var handle = GetHandle(context);
        if (handle is null) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        try
        {
            var revoked = await context.RequestServices.GetRequiredService<FoundgineGatewayApprovalService>()
                .RevokeAsync(handle, context.RequestAborted).ConfigureAwait(false);
            context.Response.StatusCode = revoked ? StatusCodes.Status204NoContent : StatusCodes.Status404NotFound;
        }
        catch (UnauthorizedAccessException) { context.Response.StatusCode = StatusCodes.Status403Forbidden; }
        catch (ArgumentException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
    }

    private static string? GetHandle(HttpContext context) => context.Request.RouteValues.TryGetValue("handle", out var value)
        ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : null;

    private static async Task<SemanticMutationOperationGraph?> ReadMutationGraphAsync(HttpContext context)
    {
        var limits = context.RequestServices.GetRequiredService<FoundgineGatewayHttpOptions>();
        var maxBodyFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodyFeature is { IsReadOnly: false }) maxBodyFeature.MaxRequestBodySize = limits.MaxRequestBytes;
        if (context.Request.ContentLength is > 0 && context.Request.ContentLength > limits.MaxRequestBytes)
        { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return null; }
        try
        {
            var request = await JsonSerializer.DeserializeAsync(context.Request.Body,
                FoundgineGatewayJsonContext.Default.FoundgineGatewayMutationRequest, context.RequestAborted).ConfigureAwait(false);
            return FoundgineGatewayMutationRequestMapper.ToGraph(request ?? throw new ArgumentException("A JSON request body is required."), limits);
        }
        catch (JsonException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return null; }
        catch (ArgumentException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return null; }
    }

    private static async Task WriteJsonAsync<T>(HttpContext context, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, value, typeInfo, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task StreamEventsAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var options = services.GetRequiredService<FoundgineGatewayEventStreamOptions>();
        var digester = services.GetRequiredService<FoundgineGatewayScopeDigester>();

        // Fail closed: the stream is audience-scoped, so the caller must have a trusted security context.
        FoundgineGatewayViewer viewer;
        try
        {
            var security = services.GetRequiredService<ISecurityExecutionContextProvider>()
                .RequireSecurityExecutionContext("Gateway", "stream events");
            var isOperator = false;
            if (options.OperatorPolicy is { } policy && services.GetService<IAuthorizationService>() is { } authorization)
                isOperator = (await authorization.AuthorizeAsync(context.User, policy).ConfigureAwait(false)).Succeeded;
            viewer = new FoundgineGatewayViewer(
                digester.Owner(security.Tenant, security.Subject), digester.Tenant(security.Tenant), isOperator);
        }
        catch (UnauthorizedAccessException)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var hub = services.GetRequiredService<FoundgineGatewayEventHub>();
        var store = services.GetRequiredService<IFoundgineGatewayEventStore>();
        var latest = await store.GetLatestSequenceAsync(context.RequestAborted).ConfigureAwait(false);
        var raw = context.Request.Headers.TryGetValue("Last-Event-ID", out var header)
            ? header.ToString() : context.Request.Query["after"].ToString();
        var cursor = FoundgineGatewayEventCursor.Resolve(raw, latest, options.MaxReplayWindow);

        context.Response.Headers["X-Foundgine-Latest-Sequence"] =
            latest.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (cursor.Status == FoundgineGatewayCursorStatus.Invalid)
        { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        if (cursor.Status == FoundgineGatewayCursorStatus.TooOld)
        { context.Response.StatusCode = StatusCodes.Status410Gone; return; }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-cache, no-store";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var token = streamCts.Token;

        async Task WriteAsync(string text)
        {
            await context.Response.WriteAsync(text, token).ConfigureAwait(false);
            await context.Response.Body.FlushAsync(token).ConfigureAwait(false);
        }

        var enumerator = hub.SubscribeAfterAsync(cursor.After, token).GetAsyncEnumerator(token);
        Task<bool>? next = null;
        try
        {
            await WriteAsync($"retry: {(long)options.ReconnectDelay.TotalMilliseconds}\n\n").ConfigureAwait(false);
            long lastScanned = cursor.After, lastCheckpoint = cursor.After;
            next = enumerator.MoveNextAsync().AsTask();
            while (true)
            {
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var delay = Task.Delay(options.HeartbeatInterval, delayCts.Token);
                var finished = await Task.WhenAny(next, delay).ConfigureAwait(false);
                if (!ReferenceEquals(finished, next))
                {
                    // Keep-alive. The id line moves the client's resume cursor past events it is not allowed to see,
                    // so a quiet tenant on a busy gateway never falls outside the replay window.
                    var checkpoint = lastScanned > lastCheckpoint
                        ? $"id: {lastScanned.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n" : string.Empty;
                    lastCheckpoint = Math.Max(lastCheckpoint, lastScanned);
                    await WriteAsync($": keepalive\n{checkpoint}\n").ConfigureAwait(false);
                    continue;
                }
                delayCts.Cancel();
                if (!await next.ConfigureAwait(false)) break;

                var gatewayEvent = enumerator.Current;
                lastScanned = gatewayEvent.Sequence;
                next = enumerator.MoveNextAsync().AsTask();
                if (!FoundgineGatewayEventVisibility.IsVisible(gatewayEvent, viewer)) continue;

                var json = JsonSerializer.Serialize(FoundgineGatewayEventVisibility.ToPublic(gatewayEvent),
                    FoundgineGatewayJsonContext.Default.FoundgineGatewayEvent);
                await WriteAsync($"id: {gatewayEvent.Sequence}\nevent: {gatewayEvent.Type}\ndata: {json}\n\n").ConfigureAwait(false);
                lastCheckpoint = gatewayEvent.Sequence;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Client disconnected or host is stopping.
        }
        finally
        {
            // An async iterator must not be disposed while a MoveNextAsync is pending.
            streamCts.Cancel();
            if (next is not null)
            {
                try { await next.ConfigureAwait(false); }
                catch (Exception) { /* cleanup only */ }
            }
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task GetExecutionStatusAsync(HttpContext context)
    {
        var id = context.Request.RouteValues.TryGetValue("id", out var value)
            ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : null;
        if (id is null || id.Length != 32 || !Guid.TryParseExact(id, "N", out _))
        { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

        var store = context.RequestServices.GetRequiredService<IFoundgineGatewayEventStore>();
        var events = await store.ReadByCorrelationIdAsync(id, 100, context.RequestAborted).ConfigureAwait(false);
        var started = events.FirstOrDefault(x => x.Type == FoundgineGatewayEventType.ExecutionStarted);
        string ownerDigest;
        try
        {
            var security = context.RequestServices.GetRequiredService<ISecurityExecutionContextProvider>()
                .RequireSecurityExecutionContext("Gateway", "read execution status");
            ownerDigest = context.RequestServices.GetRequiredService<FoundgineGatewayScopeDigester>().Owner(security.Tenant, security.Subject);
        }
        catch (UnauthorizedAccessException)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if (started is null || !started.Attributes.TryGetValue("owner.digest", out var owner) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(owner), Encoding.UTF8.GetBytes(ownerDigest)))
        { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        var latest = events.LastOrDefault();
        var status = latest?.Type switch
        {
            FoundgineGatewayEventType.ExecutionCompleted => "succeeded",
            FoundgineGatewayEventType.ExecutionFailed => "failed",
            _ => "running"
        };
        var operationKind = started.Attributes.TryGetValue("operation.kind", out var kind) ? kind : "unknown";
        string? outcomeCode = null;
        if (latest is not null && latest.Attributes.TryGetValue("outcome.code", out var code))
            outcomeCode = code;
        var response = new FoundgineGatewayExecutionStatus(id, status, operationKind,
            started.OccurredAt, status == "running" ? null : latest?.OccurredAt, outcomeCode);
        await WriteJsonAsync(context, response, FoundgineGatewayJsonContext.Default.FoundgineGatewayExecutionStatus).ConfigureAwait(false);
    }

    private static ValueTask PublishExecutionEventAsync(HttpContext context, FoundgineGatewayEventType type,
        string executionId, string operationKind, string? outcomeCode)
    {
        var publisher = context.RequestServices.GetRequiredService<IFoundgineGatewayEventPublisher>();
        var security = context.RequestServices.GetRequiredService<ISecurityExecutionContextProvider>()
            .RequireSecurityExecutionContext("Gateway", "publish execution lifecycle");
        var ownerDigest = context.RequestServices.GetRequiredService<FoundgineGatewayScopeDigester>().Owner(security.Tenant, security.Subject);
        var values = new Dictionary<string, string>
        {
            ["operation.kind"] = operationKind,
            ["owner.digest"] = ownerDigest
        };
        if (outcomeCode is not null) values["outcome.code"] = outcomeCode;
        IReadOnlyDictionary<string, string> attributes = values;
        if (type == FoundgineGatewayEventType.ExecutionStarted)
            return publisher.PublishAsync(type, "execution", executionId, attributes: attributes,
                cancellationToken: context.RequestAborted);
        return PublishBestEffortAsync(publisher, type, executionId, attributes, context.RequestAborted);
    }

    private static async ValueTask PublishBestEffortAsync(IFoundgineGatewayEventPublisher publisher,
        FoundgineGatewayEventType type, string executionId, IReadOnlyDictionary<string, string> attributes,
        CancellationToken cancellationToken)
    {
        try
        {
            await publisher.PublishAsync(type, "execution", executionId, attributes: attributes,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Preserve the original request cancellation.
        }
        catch
        {
            // Execution may have committed. A notification failure must not misreport its outcome.
        }
    }
}
