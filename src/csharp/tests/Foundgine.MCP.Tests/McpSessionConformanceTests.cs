using System.Reflection;
using System.Text.Json;
using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

/// <summary>Runs the shared mcp-session-scenarios.json (spec mcp-session-lifecycle-v1); the Java runner runs the same file.</summary>
public sealed class McpSessionConformanceTests
{
    [Fact]
    public void SharedScenariosBehaveIdenticallyInCSharp()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Foundgine.MCP.Tests.mcp-session-scenarios.json")!;
        using var doc = JsonDocument.Parse(stream);
        Assert.Equal("mcp-session-lifecycle-v1", doc.RootElement.GetProperty("spec").GetString());
        var scenarios = doc.RootElement.GetProperty("scenarios");
        Assert.True(scenarios.GetArrayLength() > 0);
        foreach (var sc in scenarios.EnumerateArray()) Run(sc);
    }

    private static string? Owner(JsonElement step) =>
        step.TryGetProperty("owner", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null;

    private static void Run(JsonElement sc)
    {
        var name = sc.GetProperty("name").GetString();
        var now = sc.GetProperty("startUnixMs").GetInt64();
        var manager = new FoundgineMcpSessionManager(
            new FoundgineMcpSessionOptions(TimeSpan.FromMilliseconds(sc.GetProperty("idleTimeoutMs").GetInt64()),
                TimeSpan.FromMilliseconds(sc.GetProperty("maximumLifetimeMs").GetInt64()), sc.GetProperty("maximumActiveSessions").GetInt32()),
            new InMemoryFoundgineMcpSessionStore(), () => now);
        var ids = new Dictionary<string, string>();
        var n = 0;
        foreach (var step in sc.GetProperty("steps").EnumerateArray())
        {
            var op = step.GetProperty("op").GetString();
            var where = $"{name} step {++n} ({op})";
            string? id = null;
            if (step.TryGetProperty("session", out var sessionRef))
            {
                var key = sessionRef.GetString()!;
                id = ids.TryGetValue(key, out var known) ? known : key;
            }
            switch (op)
            {
                case "advance": now += step.GetProperty("ms").GetInt64(); break;
                case "create":
                    var expect = step.GetProperty("expect").GetString();
                    try
                    {
                        var s = manager.Create(Owner(step));
                        Assert.True(expect == "ok", where);
                        Assert.True(step.GetProperty("expiresAt").GetInt64() == s.ExpiresAtUnixMilliseconds, where);
                        ids[step.GetProperty("as").GetString()!] = s.SessionId;
                    }
                    catch (InvalidOperationException) { Assert.True(expect == "full", where); }
                    break;
                case "validate":
                    var v = manager.Validate(id, Owner(step));
                    var expected = step.GetProperty("expect").GetString()!.Replace("_", "");
                    Assert.True(string.Equals(expected, v.Status.ToString(), StringComparison.OrdinalIgnoreCase), $"{where}: expected {expected} got {v.Status}");
                    if (v.IsValid) Assert.True(step.GetProperty("expiresAt").GetInt64() == v.Session!.ExpiresAtUnixMilliseconds, where);
                    break;
                case "terminate": Assert.True(step.GetProperty("expect").GetBoolean() == manager.Terminate(id, Owner(step)), where); break;
                case "purge": Assert.True(step.GetProperty("expect").GetInt32() == manager.PurgeExpired(), where); break;
                case "count": Assert.True(step.GetProperty("expect").GetInt32() == manager.ActiveSessionCount, where); break;
                default: Assert.Fail("unknown op in " + where); break;
            }
        }
    }
}
