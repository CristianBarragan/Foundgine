using System.Reflection;
using System.Text.Json;
using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

/// <summary>Runs the shared mcp-sse-scenarios.json (spec mcp-sse-replay-v1); the Java runner runs the same file.</summary>
public sealed class McpSseConformanceTests
{
    [Fact]
    public void SharedScenariosBehaveIdenticallyInCSharp()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Foundgine.MCP.Tests.mcp-sse-scenarios.json")!;
        using var doc = JsonDocument.Parse(stream);
        Assert.Equal("mcp-sse-replay-v1", doc.RootElement.GetProperty("spec").GetString());
        var scenarios = doc.RootElement.GetProperty("scenarios");
        Assert.True(scenarios.GetArrayLength() > 0);
        foreach (var sc in scenarios.EnumerateArray()) Run(sc);
    }

    /// <summary>The shared file writes sequences as strings (so JavaScript-safe ids survive); accept a number too.</summary>
    private static long Int64Of(JsonElement e) => e.ValueKind == JsonValueKind.String
        ? long.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
        : e.GetInt64();

    private static void Run(JsonElement sc)
    {
        var name = sc.GetProperty("name").GetString();
        var buffer = new FoundgineMcpSseBuffer(sc.GetProperty("maxEvents").GetInt32(), sc.GetProperty("maxBytes").GetInt64());
        var n = 0;
        var handles = new Dictionary<string, IDisposable>();
        var woke = false;
        var told = new List<string>();
        foreach (var step in sc.GetProperty("steps").EnumerateArray())
        {
            var op = step.GetProperty("op").GetString();
            var where = $"{name} step {++n} ({op})";
            switch (op)
            {
                case "append":
                    woke = false;
                    try
                    {
                        var e = buffer.Append(step.GetProperty("data").GetString()!);
                        Assert.True(step.GetProperty("expect").GetString() == "ok", where);
                        Assert.True(step.GetProperty("id").GetString() == e.Id, where);
                    }
                    catch (ArgumentException) { Assert.True(step.GetProperty("expect").GetString() == "rejected", where); }
                    Assert.True(step.GetProperty("woke").GetBoolean() == woke, where + " woke");
                    break;
                case "subscribe":
                    var asName = step.GetProperty("as").GetString()!;
                    var h = buffer.Subscribe(() => woke = true, () => told.Add(asName));
                    Assert.True(step.GetProperty("expect").GetString() == (h is null ? "busy" : "ok"), where);
                    if (h is not null) handles[step.GetProperty("as").GetString()!] = h;
                    break;
                case "retire":
                    told.Clear();
                    buffer.Retire();
                    Assert.True(step.GetProperty("retired").EnumerateArray().Select(i => i.GetString()!).SequenceEqual(told), where);
                    break;
                case "unsubscribe":
                    if (handles.TryGetValue(step.GetProperty("as").GetString()!, out var held)) held.Dispose();
                    break;
                case "replay":
                    string? last = step.TryGetProperty("lastEventId", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
                    var r = buffer.ReplayAfter(last);
                    var expectedStatus = step.GetProperty("expect").GetString()!;
                    Assert.True(string.Equals(expectedStatus, r.Status.ToString(), StringComparison.OrdinalIgnoreCase), $"{where}: expected {expectedStatus} got {r.Status}");
                    var expectedIds = step.GetProperty("ids").EnumerateArray().Select(i => i.GetString()!).ToList();
                    Assert.True(expectedIds.SequenceEqual(r.Events.Select(e => e.Id)), $"{where}: ids {string.Join(",", r.Events.Select(e => e.Id))}");
                    break;
                case "format":
                    var frame = FoundgineMcpSseBuffer.Format(new FoundgineMcpSseEvent(Int64Of(step.GetProperty("sequence")), step.GetProperty("data").GetString()!));
                    Assert.True(step.GetProperty("expect").GetString() == frame, where);
                    break;
                default: Assert.Fail("unknown op in " + where); break;
            }
        }
    }
}
