using System.Net;
using System.Text;
using Foundgine.Providers.Tools.MCP.Client;
using Foundgine.Core.Semantic.Capabilities;
using Foundgine.Core.Semantic.Intent;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests.Client;

public sealed class FoundgineMcpAgentClientTests
{
    [Fact]
    public async Task Discovery_then_dynamic_intent_then_execution_uses_the_discovered_contract()
    {
        var calls = new List<string>();
        var handler = new RecordingHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            calls.Add(body);
            if (body.Contains("foundgine_capabilities", StringComparison.Ordinal))
                return RpcToolResult(new SemanticCapabilityContract(1, [
                    new("Customer.read", "Read Customer", new Foundgine.Core.Abstractions.EntityId(1),
                        Foundgine.Core.Abstractions.AuthorizationDecision.Allowed, [], [], [], ["Id", "Name"],
                        ["Transactions"])
                ]));

            Assert.Contains("foundgine_query", body, StringComparison.Ordinal);
            Assert.Contains("Customer", body, StringComparison.Ordinal);
            Assert.Contains("Transactions", body, StringComparison.Ordinal);
            Assert.DoesNotContain("tenant", body, StringComparison.OrdinalIgnoreCase);
            return RpcToolResult(new { rows = Array.Empty<object>() });
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new FoundgineMcpAgentClient(http);

        await client.DiscoverAndExecuteAsync(contract =>
        {
            var capability = Assert.Single(contract.Capabilities);
            Assert.Contains("Transactions", capability.Relationships);
            return new ReadIntent("Customer", [
                new ReadSelection("Id"),
                new ReadSelection(null, "Transactions", [new ReadSelection("Id")])
            ]);
        });

        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task SSE_tool_result_is_unwrapped()
    {
        var handler = new RecordingHandler(_ => RpcToolResult(new { ok = true }, sse: true));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new FoundgineMcpAgentClient(http);

        var result = await client.ExecuteQueryAsync(
            new ReadIntent("Customer", [new ReadSelection("Id")]));

        Assert.True(result.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task MCP_error_is_not_treated_as_success()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"message\":\"denied\"}}",
                Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new FoundgineMcpAgentClient(http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ExecuteQueryAsync(new ReadIntent("Customer", [new ReadSelection("Id")])));

        Assert.Contains("denied", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage RpcToolResult(object value, bool sse = false)
    {
        var nested = System.Text.Json.JsonSerializer.Serialize(value);
        var rpc =
            $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{{\"content\":[{{\"type\":\"text\",\"text\":{System.Text.Json.JsonSerializer.Serialize(nested)}}}]}}}}";
        var body = sse ? $"event: message\ndata: {rpc}\n\n" : rpc;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, sse ? "text/event-stream" : "application/json")
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}