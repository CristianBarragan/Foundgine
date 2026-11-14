using System.Reflection;
using System.Text.Json;
using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

/// <summary>Runs the shared mcp-sse-transport-scenarios.json (spec mcp-sse-transport-v1); the Java runner runs the same file.</summary>
public sealed class McpSseTransportConformanceTests
{
    [Fact]
    public void SharedTransportScenariosBehaveIdenticallyInCSharp()
    {
        using var file = Assembly.GetExecutingAssembly().GetManifestResourceStream("Foundgine.MCP.Tests.mcp-sse-transport-scenarios.json")!;
        using var doc = JsonDocument.Parse(file);
        Assert.Equal("mcp-sse-transport-v1", doc.RootElement.GetProperty("spec").GetString());
        var scenarios = doc.RootElement.GetProperty("scenarios");
        Assert.True(scenarios.GetArrayLength() > 0);
        foreach (var sc in scenarios.EnumerateArray()) Run(sc);
    }

    private sealed class RecordingSink : IFoundgineMcpSseSink
    {
        public readonly List<string> Frames = new();
        public bool Completed;
        public void Write(string frame) => Frames.Add(frame);
        public void Complete() => Completed = true;
    }

    private sealed class ListAudit : IFoundgineMcpAuditSink
    {
        public readonly List<string> Entries = new();
        public void Record(FoundgineMcpAuditEvent e)
        {
            var type = e.Type.ToString().ToLowerInvariant();
            if (type.StartsWith("sse", StringComparison.Ordinal)) Entries.Add(type + ":" + e.Actor);
        }
    }

    private static string? Text(JsonElement step, string field) =>
        step.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(x => x.GetString()!).ToList();

    private static void Pump(Queue<Action> queue) { while (queue.TryDequeue(out var work)) work(); }

    private static void Run(JsonElement sc)
    {
        var name = sc.GetProperty("name").GetString();
        var origins = Strings(sc.GetProperty("origins"));
        var audit = new ListAudit();
        var gate = new FoundgineMcpSessionGate(new FoundgineMcpSessionManager(), audit);
        var registry = new FoundgineMcpSseRegistry(1000, sc.GetProperty("maxEvents").GetInt32(), 1_000_000L);
        var settings = new FoundgineMcpSseTransport.Settings(origins, 0,
            new FoundgineMcpSseLimits(sc.GetProperty("maxTotal").GetInt32(), sc.GetProperty("maxPerOwner").GetInt32()), audit);
        var queue = new Queue<Action>();
        var ids = new Dictionary<string, string>();
        var streams = new Dictionary<string, FoundgineMcpSseStream>();
        var sinks = new Dictionary<string, RecordingSink>();
        var n = 0;
        foreach (var step in sc.GetProperty("steps").EnumerateArray())
        {
            var op = step.GetProperty("op").GetString();
            var where = $"{name} step {++n} ({op})";
            var handle = Text(step, "session");
            var sessionId = handle is null ? null : ids.GetValueOrDefault(handle, handle);
            var owner = Text(step, "owner");
            switch (op)
            {
                case "session":
                    var outcome = gate.AdmitPost("initialize", null, owner);
                    Assert.True(outcome.HttpStatus == 200, where);
                    ids[step.GetProperty("as").GetString()!] = outcome.IssuedSessionId!;
                    registry.Stream(outcome.IssuedSessionId!);
                    break;
                case "publish":
                    registry.Stream(sessionId!).Append(step.GetProperty("data").GetString()!);
                    break;
                case "get":
                    var rec = new RecordingSink();
                    var result = FoundgineMcpSseTransport.Get(gate, registry, settings, Text(step, "origin"), sessionId, owner,
                        Text(step, "lastEventId"), rec, work => queue.Enqueue(work));
                    Assert.True(step.GetProperty("expect").GetInt32() == result.Status, $"{where}: expected {step.GetProperty("expect").GetInt32()} got {result.Status}");
                    if (result.Status == 200)
                    {
                        var key = step.GetProperty("as").GetString()!;
                        streams[key] = result.Stream!;
                        sinks[key] = rec;
                        result.Stream!.Start();
                    }
                    break;
                case "delete":
                    var status = FoundgineMcpSseTransport.Delete(gate, registry, settings, Text(step, "origin"), sessionId, owner);
                    Assert.True(step.GetProperty("expect").GetInt32() == status, $"{where}: got {status}");
                    break;
                case "heartbeat":
                    streams[step.GetProperty("stream").GetString()!].Heartbeat();
                    break;
                case "frames":
                    Pump(queue);
                    var r = sinks[step.GetProperty("stream").GetString()!];
                    Assert.True(Strings(step.GetProperty("expect")).SequenceEqual(r.Frames), $"{where}: got {string.Join("|", r.Frames)}");
                    r.Frames.Clear();
                    break;
                case "closed":
                    Pump(queue);
                    Assert.True(step.GetProperty("expect").GetBoolean() == sinks[step.GetProperty("stream").GetString()!].Completed, where);
                    break;
                case "audit":
                    Pump(queue);
                    Assert.True(Strings(step.GetProperty("expect")).SequenceEqual(audit.Entries), $"{where}: got {string.Join(",", audit.Entries)}");
                    audit.Entries.Clear();
                    break;
                default: Assert.Fail("unknown op in " + where); break;
            }
            Pump(queue);
        }
    }
}
