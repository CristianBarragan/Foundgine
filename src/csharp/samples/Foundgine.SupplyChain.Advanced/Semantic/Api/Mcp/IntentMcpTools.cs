using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Foundgine.SupplyChain.Advanced.Intent;

/// <summary>
/// The <c>foundgine_intent</c> MCP tool: the same canonical intent service as <c>POST /intent</c>. The caller
/// (tenant and role) comes from the <c>X-Foundgine-*</c> headers of the <c>/mcp</c> request, never from the
/// tool arguments, so a model cannot choose its own identity.
/// </summary>
[McpServerToolType]
public sealed class IntentMcpTools(SupplyChainIntentService service, IHttpContextAccessor http)
{
    [McpServerTool(Name = "foundgine_intent")]
    [Description(
        "Run a canonical, provider-neutral read intent through Foundgine as the current caller. " +
        "intent is {rootEntity, selections, filter?, order?, limit?, offset?} in semantic names, never SQL. " +
        "mode is execute (default), explain (dry run: resolution, pruned fields, plan) or capabilities " +
        "(what the caller may read; intent is not needed). Identity is never part of the intent.")]
    public async Task<CallToolResult> Intent(
        JsonElement? intent = null,
        string mode = "execute",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = http.HttpContext?.Request
                          ?? throw new InvalidOperationException("The intent tool requires an HTTP request.");
            var caller = IntentEndpoint.ResolveCaller(request, service);
            var text = await IntentEndpoint.CallToolAsync(service, caller, intent, mode, cancellationToken);
            return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // An explicit error result, not a thrown exception: the SDK wraps thrown exceptions in its own
            // "An error occurred invoking ..." text, which would hide the classification (for example
            // "forbidden: ...") from the client. ToolFailureMessage never leaks internals.
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = IntentEndpoint.ToolFailureMessage(e) }]
            };
        }
    }
}