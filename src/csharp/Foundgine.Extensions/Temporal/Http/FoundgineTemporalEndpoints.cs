using System.Text.Json;
using System.Text.Json.Serialization;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Extensions.Temporal.Contracts;
using Foundgine.Extensions.Temporal.Hosting;
using Foundgine.Extensions.Temporal.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace Foundgine.Extensions.Temporal.Http;

public sealed record StartWorkflowBody(string BusinessKey, JsonElement Payload);
public sealed record DecisionBody(string Step, bool Approved, string? Reason);
public sealed record StartWorkflowResponse(string WorkflowId, string Status, bool AlreadyExisted);
public sealed record WorkflowStatusResponse(string WorkflowId, string TemporalStatus, FoundgineProcessStatus? Process);
public sealed record TemporalErrorResponse(string Error);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StartWorkflowBody))]
[JsonSerializable(typeof(DecisionBody))]
[JsonSerializable(typeof(StartWorkflowResponse))]
[JsonSerializable(typeof(WorkflowStatusResponse))]
[JsonSerializable(typeof(TemporalErrorResponse))]
public sealed partial class FoundgineTemporalJsonContext : JsonSerializerContext;

public sealed class FoundgineTemporalHttpOptions
{
    /// <summary>Authorization policy required to approve/reject a step. Strongly recommended in production.</summary>
    public string? ApprovalPolicy { get; set; }
    public int MaxPayloadBytes { get; set; } = 64 * 1024;
}

public static class FoundgineTemporalEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps start / status / decision / cancel endpoints. Tenant and subject come only from the host's trusted
    /// <see cref="ISecurityExecutionContextProvider"/>; the workflow id is derived from them, so another tenant's
    /// workflow cannot be addressed even by guessing a business key. Every route requires authorization.
    /// </summary>
    public static IEndpointRouteBuilder MapFoundgineTemporal(
        this IEndpointRouteBuilder endpoints, string routePrefix = "/foundgine/workflows",
        Action<FoundgineTemporalHttpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var http = new FoundgineTemporalHttpOptions();
        configure?.Invoke(http);
        var prefix = routePrefix.TrimEnd('/');

        endpoints.MapPost($"{prefix}/{{process}}", (RequestDelegate)(ctx => StartAsync(ctx, http))).RequireAuthorization();
        endpoints.MapGet($"{prefix}/{{process}}/{{key}}", (RequestDelegate)StatusAsync).RequireAuthorization();
        endpoints.MapPost($"{prefix}/{{process}}/{{key}}/cancel", (RequestDelegate)CancelAsync).RequireAuthorization();
        var decision = endpoints.MapPost($"{prefix}/{{process}}/{{key}}/decision", (RequestDelegate)DecideAsync).RequireAuthorization();
        if (!string.IsNullOrWhiteSpace(http.ApprovalPolicy)) decision.RequireAuthorization(http.ApprovalPolicy);
        return endpoints;
    }

    private static async Task StartAsync(HttpContext ctx, FoundgineTemporalHttpOptions http)
    {
        var security = Security(ctx, "workflow start");
        var body = await ReadAsync(ctx, FoundgineTemporalJsonContext.Default.StartWorkflowBody).ConfigureAwait(false);
        if (body is null) { await ErrorAsync(ctx, 400, "Request body is required."); return; }

        var payload = body.Payload.ValueKind == JsonValueKind.Undefined ? "{}" : body.Payload.GetRawText();
        if (System.Text.Encoding.UTF8.GetByteCount(payload) > http.MaxPayloadBytes)
        { await ErrorAsync(ctx, 413, "Payload too large."); return; }

        try
        {
            if (security.Tenant != null)
            {
                var result = await ctx.RequestServices.GetRequiredService<IFoundgineWorkflowStarter>().StartAsync(
                    new FoundgineWorkflowStartRequest(
                        security.Tenant, security.Subject, (string)ctx.Request.RouteValues["process"]!, body.BusinessKey,
                        payload, CorrelationId: ctx.TraceIdentifier), ctx.RequestAborted).ConfigureAwait(false);

                await WriteAsync(ctx, result.AlreadyExisted ? 200 : 202,
                    new StartWorkflowResponse(result.WorkflowId, result.AlreadyExisted ? "Existing" : "Started", result.AlreadyExisted),
                    FoundgineTemporalJsonContext.Default.StartWorkflowResponse, location: $"{ctx.Request.Path.Value}/{body.BusinessKey}");
            }
        }
        catch (ArgumentException ex)
        {
            await ErrorAsync(ctx, 400, ex.Message);
        }
    }

    private static async Task StatusAsync(HttpContext ctx)
    {
        var (handle, id) = Handle(ctx, "workflow status");
        try
        {
            var description = await handle.DescribeAsync().ConfigureAwait(false);
            FoundgineProcessStatus? process = null;
            if (description.Status == Temporalio.Api.Enums.V1.WorkflowExecutionStatus.Running)
                process = await handle.QueryAsync(wf => wf.GetStatus()).ConfigureAwait(false);

            await WriteAsync(ctx, 200, new WorkflowStatusResponse(id, description.Status.ToString(), process),
                FoundgineTemporalJsonContext.Default.WorkflowStatusResponse);
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            await ErrorAsync(ctx, 404, "Workflow not found.");
        }
    }

    private static async Task CancelAsync(HttpContext ctx)
    {
        var (handle, _) = Handle(ctx, "workflow cancel");
        try
        {
            await handle.CancelAsync().ConfigureAwait(false);
            ctx.Response.StatusCode = 202;
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            await ErrorAsync(ctx, 404, "Workflow not found.");
        }
    }

    private static async Task DecideAsync(HttpContext ctx)
    {
        var security = Security(ctx, "workflow decision");
        var (handle, _) = Handle(ctx, "workflow decision");
        var body = await ReadAsync(ctx, FoundgineTemporalJsonContext.Default.DecisionBody).ConfigureAwait(false);
        if (body is null || string.IsNullOrWhiteSpace(body.Step)) { await ErrorAsync(ctx, 400, "Step is required."); return; }
        try
        {
            // DecidedBy is stamped from the trusted context, never from the request body.
            await handle.SignalAsync(wf => wf.DecideAsync(
                new FoundgineApprovalDecision(body.Step, body.Approved, body.Reason, security.Subject))).ConfigureAwait(false);
            ctx.Response.StatusCode = 202;
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            await ErrorAsync(ctx, 404, "Workflow not found.");
        }
    }

    private static SecurityExecutionContext Security(HttpContext ctx, string operation) =>
        ctx.RequestServices.GetRequiredService<ISecurityExecutionContextProvider>()
            .RequireSecurityExecutionContext("Temporal", operation);

    private static (WorkflowHandle<FoundgineProcessWorkflow> Handle, string Id) Handle(HttpContext ctx, string operation)
    {
        var security = Security(ctx, operation);
        if (security.Tenant != null)
        {
            var id = ctx.RequestServices.GetRequiredService<IFoundgineWorkflowStarter>().WorkflowId(
                security.Tenant, (string)ctx.Request.RouteValues["process"]!, (string)ctx.Request.RouteValues["key"]!);
            return (ctx.RequestServices.GetRequiredService<ITemporalClient>().GetWorkflowHandle<FoundgineProcessWorkflow>(id), id);
        }

        return default;
    }

    private static async Task<T?> ReadAsync<T>(HttpContext ctx, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) where T : class
    {
        try { return await JsonSerializer.DeserializeAsync(ctx.Request.Body, info, ctx.RequestAborted).ConfigureAwait(false); }
        catch (JsonException) { return null; }
    }

    private static async Task WriteAsync<T>(
        HttpContext ctx, int status, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info, string? location = null)
    {
        ctx.Response.StatusCode = status;
        if (location is not null) ctx.Response.Headers.Location = location;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, value, info, ctx.RequestAborted).ConfigureAwait(false);
    }

    private static Task ErrorAsync(HttpContext ctx, int status, string message) =>
        WriteAsync(ctx, status, new TemporalErrorResponse(message), FoundgineTemporalJsonContext.Default.TemporalErrorResponse);
}
