using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Foundgine.RedTeam.Security;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = Options.Parse(args);

        if (!options.IsLocalTargetAllowed())
        {
            Console.Error.WriteLine("Refusing target. This red-team harness only runs against localhost/loopback by default.");
            Console.Error.WriteLine("Use --allow-private only for an explicitly controlled private test environment.");
            return 2;
        }

        using var http = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var model = new AttackModel(options);
        var attacker = new RedTeamAgent(http, model, options);

        Console.WriteLine("Foundgine Red Team AI");
        Console.WriteLine($"Target: {options.BaseUrl}");
        Console.WriteLine($"Surface: {options.Surface}");
        Console.WriteLine($"Profile: {options.Profile}");
        Console.WriteLine($"Rounds: {options.Rounds}");
        Console.WriteLine();

        var report = await attacker.RunAsync();

        var output = Path.GetFullPath(options.Output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine();
        Console.WriteLine($"Report: {output}");
        Console.WriteLine($"Findings: {report.Findings.Count}");
        Console.WriteLine($"Requests: {report.Requests.Count}");

        return report.HasCriticalFinding ? 1 : 0;
    }
}

public sealed record Options(
    string BaseUrl,
    string Surface,
    string Profile,
    int Rounds,
    int TimeoutSeconds,
    string Output,
    bool AllowPrivate,
    string? ModelEndpoint,
    string? ModelName,
    string? ModelApiKey)
{
    public static Options Parse(string[] args)
    {
        string Get(string name, string fallback)
        {
            var i = Array.FindIndex(args, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        int GetInt(string name, int fallback) =>
            int.TryParse(Get(name, fallback.ToString()), out var v) ? v : fallback;

        var baseUrl = Get("--base-url", Environment.GetEnvironmentVariable("FOUNDGINE_REDTEAM_URL") ?? "http://127.0.0.1:5080");
        var surface = Get("--surface", "both");
        var rounds = Math.Clamp(GetInt("--rounds", 24), 1, 200);
        var profile = Get("--profile", "semantic").ToLowerInvariant();
        var timeout = Math.Clamp(GetInt("--timeout", 10), 1, 60);
        var output = Get("--output", "artifacts/redteam-report.json");

        return new Options(
            baseUrl.TrimEnd('/') + "/",
            surface.ToLowerInvariant(),
            profile,
            rounds,
            timeout,
            output,
            args.Any(x => x.Equals("--allow-private", StringComparison.OrdinalIgnoreCase)),
            Environment.GetEnvironmentVariable("REDTEAM_MODEL_ENDPOINT"),
            Environment.GetEnvironmentVariable("REDTEAM_MODEL_NAME"),
            Environment.GetEnvironmentVariable("REDTEAM_MODEL_API_KEY"));
    }

    public bool IsLocalTargetAllowed()
    {
        if (AllowPrivate) return true;
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)) return false;
        return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class RedTeamAgent
{
    private readonly HttpClient _http;
    private readonly AttackModel _model;
    private readonly Options _options;
    private readonly List<RequestRecord> _requests = [];
    private readonly List<Finding> _findings = [];
    private readonly Dictionary<string, string> _observations = new();

    public RedTeamAgent(HttpClient http, AttackModel model, Options options)
    {
        _http = http;
        _model = model;
        _options = options;
    }

    public async Task<RedTeamReport> RunAsync()
    {
        var catalog = AttackCatalog.Build(_options.Surface, _options.Profile);

        // Baseline first: the agent needs to know what a normal successful
        // read/mutation looks like before interpreting an attack.
        await BaselineAsync(catalog);

        var candidates = await _model.PlanAsync(catalog, _observations, _options.Rounds);

        foreach (var attack in candidates.Take(_options.Rounds))
        {
            try
            {
                var result = await ExecuteAsync(attack);
                _requests.Add(result.Request);

                var findings = Oracle.Evaluate(attack, result, _observations);
                _findings.AddRange(findings);

                Console.WriteLine(
                    $"[{(findings.Count == 0 ? "PASS" : "FINDING")}] " +
                    $"{attack.Id}: {attack.Name}");

                foreach (var finding in findings)
                    Console.WriteLine($"  -> {finding.Severity}: {finding.Title}");
            }
            catch (Exception ex)
            {
                _requests.Add(new RequestRecord(
                    attack.Id, attack.Surface, attack.Name, "EXCEPTION",
                    ex.Message, false, null));
            }
        }

        return new RedTeamReport(
            DateTimeOffset.UtcNow,
            _options.BaseUrl,
            _options.Surface,
            _requests,
            _findings
                .GroupBy(x => x.Fingerprint)
                .Select(x => x.First())
                .ToList());
    }

    private async Task BaselineAsync(IReadOnlyList<AttackCase> catalog)
    {
        foreach (var attack in catalog.Where(x => x.IsBaseline).Take(2))
        {
            var result = await ExecuteAsync(attack);
            _requests.Add(result.Request);
            _observations[attack.Id] = result.Body;
        }
    }

    private async Task<AttackResult> ExecuteAsync(AttackCase attack)
    {
        using var request = new HttpRequestMessage(
            new HttpMethod(attack.Method),
            attack.Path);

        request.Headers.TryAddWithoutValidation("X-Red-Team", "Foundgine-RedTeam-AI");

        if (attack.Body is not null)
            request.Content = new StringContent(
                JsonSerializer.Serialize(attack.Body),
                Encoding.UTF8,
                "application/json");

        var started = Stopwatch.GetTimestamp();
        using var response = await _http.SendAsync(request);
        var elapsed = Stopwatch.GetElapsedTime(started);
        var body = await response.Content.ReadAsStringAsync();

        var record = new RequestRecord(
            attack.Id,
            attack.Surface,
            attack.Name,
            $"{(int)response.StatusCode} {response.StatusCode}",
            Truncate(body, 3000),
            response.IsSuccessStatusCode,
            elapsed.TotalMilliseconds);

        return new AttackResult(attack, record, response.StatusCode, body);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";
}

public sealed class AttackModel
{
    private readonly Options _options;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AttackModel(Options options) => _options = options;

    public async Task<IReadOnlyList<AttackCase>> PlanAsync(
        IReadOnlyList<AttackCase> catalog,
        IReadOnlyDictionary<string, string> observations,
        int rounds)
    {
        // If an OpenAI-compatible endpoint is configured, the model ranks the
        // bounded attack catalog. It cannot invent arbitrary HTTP operations.
        if (!string.IsNullOrWhiteSpace(_options.ModelEndpoint))
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                if (!string.IsNullOrWhiteSpace(_options.ModelApiKey))
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue(
                            "Bearer", _options.ModelApiKey);

                var prompt = """
You are a defensive application-security red-team planner.
The target is an explicitly authorized local test instance of Foundgine.
Choose only attack IDs from the supplied catalog. Do not invent endpoints,
shell commands, credentials, malware, persistence, exfiltration, or destructive
actions. Prioritize authorization bypass, IDOR, tenant isolation, mutation
integrity, injection handling, replay/idempotency, information leakage, and
MCP/GraphQL boundary confusion.

Return JSON only:
{"attackIds":["id1","id2",...]}

CATALOG:
""" + JsonSerializer.Serialize(catalog.Select(x => new {
                    x.Id, x.Name, x.Surface, x.Risk, x.Description
                }));

                var payload = new
                {
                    model = _options.ModelName ?? "red-team",
                    messages = new[]
                    {
                        new { role = "system", content = "You are a defensive security testing planner." },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.1
                };

                using var response = await client.PostAsJsonAsync(_options.ModelEndpoint, payload);
                response.EnsureSuccessStatusCode();
                var text = await response.Content.ReadAsStringAsync();

                var root = JsonNode.Parse(text);
                var content = root?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
                              ?? root?["content"]?.GetValue<string>();

                if (!string.IsNullOrWhiteSpace(content))
                {
                    var plan = JsonSerializer.Deserialize<AttackPlan>(content, JsonOptions);
                    if (plan?.AttackIds is not null)
                    {
                        var byId = catalog.ToDictionary(x => x.Id);
                        var selected = plan.AttackIds
                            .Where(byId.ContainsKey)
                            .Select(id => byId[id])
                            .DistinctBy(x => x.Id)
                            .Take(rounds)
                            .ToList();

                        if (selected.Count > 0)
                            return selected;
                    }
                }
            }
            catch
            {
                // Fall back to deterministic coverage. A security test should
                // never disappear because the optional LLM is unavailable.
            }
        }

        return catalog
            .OrderByDescending(x => x.Risk)
            .ThenBy(x => x.Id)
            .Take(rounds)
            .ToList();
    }
}

public static class AttackCatalog
{
    public static List<AttackCase> Build(string surface, string profile)
    {
        if (profile.Equals("execution", StringComparison.OrdinalIgnoreCase))
            return ExecutionMcp();

        return SemanticMcp();
    }

    // This endpoint is the semantic authorization lab under
    // samples/Foundgine.SupplyChain.Advanced/Semantic/Api/Mcp.
    private static List<AttackCase> SemanticMcp() =>
    [
        new("sem-baseline", "Authenticated capability discovery", "mcp", 1,
            "Known-good authenticated call establishes the response shape.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 1, method = "tools/call",
                @params = new { name = "describe_capabilities", arguments = new { actor = "alice", token = "alice-demo-token" } } }, true),

        new("sem-wrong-token", "Wrong-token authentication bypass", "mcp", 10,
            "A known actor with a wrong token must not authenticate.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 2, method = "tools/call",
                @params = new { name = "describe_capabilities", arguments = new { actor = "alice", token = "bob-demo-token" } } }, false),

        new("sem-unknown-actor", "Unknown actor authentication", "mcp", 9,
            "An unknown actor must not authenticate.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 3, method = "tools/call",
                @params = new { name = "describe_capabilities", arguments = new { actor = "root", token = "root" } } }, false),

        new("sem-spoof-role", "Identity claim spoofing", "mcp", 10,
            "Caller-supplied role claims must not elevate identity.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 4, method = "tools/call",
                @params = new { name = "write_entity", arguments = new { actor = "alice", token = "alice-demo-token",
                    entity = "InventoryLot", operation = "update",
                    claims = new Dictionary<string,string> { ["role"] = "SupplyChainManager", ["tenant"] = "tenant-b" } } } }, false),

        new("sem-cross-tenant", "Cross-tenant policy probe", "mcp", 10,
            "Tenant-a customer must not obtain tenant-b data predicate access.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 5, method = "tools/call",
                @params = new { name = "policy_probe", arguments = new { actor = "alice", token = "alice-demo-token", attack = "cross-tenant" } } }, false),

        new("sem-sensitive-field", "Sensitive field authorization", "mcp", 9,
            "A customer must not gain access to a sensitive inventory field.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 6, method = "tools/call",
                @params = new { name = "policy_probe", arguments = new { actor = "alice", token = "alice-demo-token", attack = "sensitive-field" } } }, false),

        new("sem-relationship-escalation", "Relationship authorization escalation", "mcp", 9,
            "A customer must not obtain a restricted supplier relationship.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 7, method = "tools/call",
                @params = new { name = "policy_probe", arguments = new { actor = "alice", token = "alice-demo-token", attack = "relationship-escalation" } } }, false),

        new("sem-write-escalation", "Entity write escalation", "mcp", 10,
            "A customer must not gain write access to InventoryLot.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 8, method = "tools/call",
                @params = new { name = "policy_probe", arguments = new { actor = "alice", token = "alice-demo-token", attack = "write-escalation" } } }, false),

        new("sem-named-operation", "Named operation escalation", "mcp", 10,
            "A caller must not elevate via a named operation such as inventory.reconcile.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 9, method = "tools/call",
                @params = new { name = "policy_probe", arguments = new { actor = "alice", token = "alice-demo-token", attack = "named-operation" } } }, false),

        new("sem-claim-narrowing", "Claim boundary manipulation", "mcp", 8,
            "Accepted claims must not widen the authenticated authorization policy.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 10, method = "tools/call",
                @params = new { name = "policy_probe", arguments = new { actor = "alice", token = "alice-demo-token", attack = "claims-scope-narrowing",
                    claims = new Dictionary<string,string> { ["scope"] = "*", ["warehouse"] = "all" } } } }, false),
    ];

    // This is the execution sample under samples/.../Semantic/Api/Mcp. It uses
    // actor-based authorization rather than token authentication.
    private static List<AttackCase> ExecutionMcp() =>
    [
        new("exec-baseline", "Authenticated execution baseline", "mcp", 1,
            "Known-good product read establishes the execution response shape.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 101, method = "tools/call",
                @params = new { name = "get_product", arguments = new { actor = "alice", productId = 1 } } }, true),

        new("exec-customer-inventory", "Customer to warehouse capability escalation", "mcp", 10,
            "Customer must not invoke warehouse inventory mutation.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 102, method = "tools/call",
                @params = new { name = "update_inventory", arguments = new { actor = "alice", warehouseId = 1, productId = 1, quantity = 999 } } }, false),

        new("exec-customer-list-suppliers", "Customer supplier enumeration", "mcp", 9,
            "Customer must not enumerate supplier data.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 103, method = "tools/call",
                @params = new { name = "list_suppliers", arguments = new { actor = "alice" } } }, false),

        new("exec-customer-shipment-write", "Customer shipment write escalation", "mcp", 10,
            "Customer must not create a shipment.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 104, method = "tools/call",
                @params = new { name = "create_shipment", arguments = new { actor = "alice", orderId = 1001, carrierId = 1, warehouseId = 1,
                    trackingNumber = "redteam-test-no-write" } } }, false),

        new("exec-customer-cross-customer-order", "Cross-customer order access", "mcp", 10,
            "Alice must not read Bob's customer scope.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 105, method = "tools/call",
                @params = new { name = "get_order", arguments = new { actor = "alice", customerId = 2, orderId = 1001 } } }, false),

        new("exec-unknown-actor", "Unknown actor authorization", "mcp", 9,
            "Unknown actors must fail closed.",
            "POST", "mcp", new { jsonrpc = "2.0", id = 106, method = "tools/call",
                @params = new { name = "update_inventory", arguments = new { actor = "root", warehouseId = 1, productId = 1, quantity = 999 } } }, false),
    ];
}

public static class Oracle
{
    public static List<Finding> Evaluate(
        AttackCase attack,
        AttackResult result,
        IReadOnlyDictionary<string, string> observations)
    {
        var body = result.Body;
        var lower = body.ToLowerInvariant();
        var findings = new List<Finding>();

        var authAttack = attack.Id.Contains("auth", StringComparison.OrdinalIgnoreCase)
                      || attack.Id.Contains("wrong-token", StringComparison.OrdinalIgnoreCase)
                      || attack.Id.Contains("unknown-actor", StringComparison.OrdinalIgnoreCase)
                      || attack.Id.Contains("idor", StringComparison.OrdinalIgnoreCase)
                      || attack.Id.Contains("capability-escalation", StringComparison.OrdinalIgnoreCase);

        if (authAttack && result.StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices)
        {
            // A successful HTTP response can still encode an application-level
            // authorization error. Treat obvious authorization markers as safe.
            var denied = lower.Contains("not authorized")
                      || lower.Contains("authorization denied")
                      || lower.Contains("semantic authorization denied")
                      || lower.Contains("invalid actor credentials")
                      || lower.Contains("forbidden")
                      || lower.Contains("unauthorized")
                      || lower.Contains("\"iserror\":true")
                      || lower.Contains("error") && lower.Contains("jsonrpc");

            if (!denied)
            {
                findings.Add(NewFinding(
                    "AUTH-BYPASS",
                    "High",
                    "Authorization attack appears to have been accepted",
                    $"Attack '{attack.Name}' returned success without an obvious denial marker."));
            }
        }

        if (attack.Id.Contains("sqli", StringComparison.OrdinalIgnoreCase))
        {
            if (lower.Contains("syntax error")
                || lower.Contains("postgres")
                || lower.Contains("sqlstate")
                || lower.Contains("npgsql")
                || lower.Contains("stack trace"))
            {
                findings.Add(NewFinding(
                    "SQLI-ERROR-LEAK",
                    "High",
                    "Injection-shaped input triggered a database/error leak",
                    "The response contains database/provider error indicators."));
            }
        }

        if (attack.Id.Contains("stacktrace", StringComparison.OrdinalIgnoreCase))
        {
            if (lower.Contains(".cs:line") || lower.Contains(" at system.") || lower.Contains("stacktrace"))
            {
                findings.Add(NewFinding(
                    "ERROR-LEAK",
                    "Medium",
                    "Authorization failure leaks internal exception details",
                    "Source/stack-trace markers were returned to the caller."));
            }
        }

        if (attack.Id.Contains("negative", StringComparison.OrdinalIgnoreCase)
            || attack.Id.Contains("huge", StringComparison.OrdinalIgnoreCase))
        {
            if (result.StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices
                && !lower.Contains("not authorized")
                && !lower.Contains("validation")
                && !lower.Contains("invalid")
                && !lower.Contains("error"))
            {
                findings.Add(NewFinding(
                    "BUSINESS-INVARIANT",
                    "High",
                    "Dangerous mutation value appears accepted",
                    "The bounded red-team request returned success without a validation/error signal."));
            }
        }

        if (attack.Id.Contains("introspection", StringComparison.OrdinalIgnoreCase)
            && result.StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices)
        {
            findings.Add(NewFinding(
                "INFO-INTROSPECTION",
                "Info",
                "GraphQL introspection is enabled",
                "This is not automatically a vulnerability; review whether production policy permits it."));
        }

        return findings;
    }

    private static Finding NewFinding(string id, string severity, string title, string evidence) =>
        new(
            id,
            severity,
            title,
            evidence,
            $"{id}|{title}");
}

public sealed record AttackCase(
    string Id,
    string Name,
    string Surface,
    int Risk,
    string Description,
    string Method,
    string Path,
    object? Body,
    bool IsBaseline);

public sealed record AttackResult(
    AttackCase Attack,
    RequestRecord Request,
    HttpStatusCode StatusCode,
    string Body);

public sealed record RequestRecord(
    string AttackId,
    string Surface,
    string Name,
    string Status,
    string Response,
    bool HttpSuccess,
    double? ElapsedMs);

public sealed record Finding(
    string Id,
    string Severity,
    string Title,
    string Evidence,
    string Fingerprint);

public sealed record RedTeamReport(
    DateTimeOffset StartedAtUtc,
    string Target,
    string Surface,
    IReadOnlyList<RequestRecord> Requests,
    IReadOnlyList<Finding> Findings)
{
    public bool HasCriticalFinding =>
        Findings.Any(x => x.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase));
}

public sealed record AttackPlan(List<string>? AttackIds);
