using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var url = args.Length > 0 ? args[0] : "http://localhost:4782/mcp";
using var client = new HttpClient();

Console.WriteLine("Foundgine SupplyChain — MCP authorization adversarial client");
Console.WriteLine("===========================================================");
Console.WriteLine($"Endpoint: {url}");
Console.WriteLine();

var cases = new (string, string, object)[]
{
    ("capabilities", "describe_capabilities", new { actor = "analyst-a", token = "analyst-a-demo-token" }),
    ("cross-tenant read", "policy_probe",
        new { actor = "analyst-a", token = "analyst-a-demo-token", attack = "cross-tenant" }),
    ("sensitive field", "policy_probe",
        new { actor = "analyst-a", token = "analyst-a-demo-token", attack = "sensitive-field" }),
    ("relationship escalation", "policy_probe",
        new { actor = "operator-a", token = "operator-a-demo-token", attack = "relationship-escalation" }),
    ("write escalation", "policy_probe",
        new { actor = "analyst-a", token = "analyst-a-demo-token", attack = "write-escalation" }),
    ("named operation escalation", "policy_probe",
        new { actor = "operator-a", token = "operator-a-demo-token", attack = "named-operation" }),
    ("unauthorized customer write", "write_entity",
        new { actor = "alice", token = "alice-demo-token", entity = "InventoryLot", operation = "update" }),
    ("wrong token", "describe_capabilities", new { actor = "alice", token = "manager-a-demo-token" }),
    ("unknown actor", "describe_capabilities", new { actor = "unknown-agent", token = "whatever" }),
    ("authorized operator write", "write_entity",
        new { actor = "operator-a", token = "operator-a-demo-token", entity = "InventoryLot", operation = "update" }),

    // --- Client-supplied claims: attacks -------------------------------
    // These calls authenticate as an ordinary actor/token pair (unchanged),
    // then attach an untrusted claim set to the same call. Identity claims
    // must never be honored regardless of role, and evidence/scope claims
    // must never be able to widen what the role already allows.
    ("claims: role injection attempt", "write_entity", new
    {
        actor = "alice", token = "alice-demo-token", entity = "InventoryLot", operation = "update",
        claims = new Dictionary<string, string> { ["role"] = "SupplyChainManager" }
    }),
    ("claims: tenant injection attempt", "policy_probe", new
    {
        actor = "analyst-a", token = "analyst-a-demo-token", attack = "cross-tenant",
        claims = new Dictionary<string, string> { ["tenant"] = "tenant-b" }
    }),
    ("claims: reconcile without evidence", "policy_probe", new
    {
        actor = "manager-a", token = "manager-a-demo-token", attack = "claims-reconcile"
    }),
    ("claims: reconcile with malformed change ticket", "policy_probe", new
    {
        actor = "manager-a", token = "manager-a-demo-token", attack = "claims-reconcile",
        claims = new Dictionary<string, string>
        {
            ["reason"] = "Quarterly cycle count discrepancy",
            ["change_ticket"] = "not-a-ticket"
        }
    }),
    ("claims: reconcile with expired evidence", "policy_probe", new
    {
        actor = "manager-a", token = "manager-a-demo-token", attack = "claims-reconcile",
        claims = new Dictionary<string, string>
        {
            ["reason"] = "Quarterly cycle count discrepancy",
            ["change_ticket"] = "CHG-4821",
            ["not_after"] = "2020-01-01T00:00:00Z"
        }
    }),

    // --- Client-supplied claims: legitimate, self-narrowing uses --------
    // These are not attacks. They demonstrate claims a well-behaved caller
    // sends to *restrict* itself, and confirm the policy honors that
    // restriction rather than silently ignoring it.
    ("claims: self-imposed read-only scope", "policy_probe", new
    {
        actor = "manager-a", token = "manager-a-demo-token", attack = "claims-scope-narrowing",
        claims = new Dictionary<string, string> { ["scope"] = "read-only" }
    }),
    ("claims: warehouse scoping narrows predicate", "policy_probe", new
    {
        actor = "operator-a", token = "operator-a-demo-token", attack = "claims-warehouse-scoping",
        claims = new Dictionary<string, string> { ["warehouse"] = "12" }
    }),
    ("claims: unknown claim key ignored", "policy_probe", new
    {
        actor = "analyst-a", token = "analyst-a-demo-token", attack = "cross-tenant",
        claims = new Dictionary<string, string> { ["favorite_color"] = "blue" }
    }),
    ("claims: valid reconcile evidence", "policy_probe", new
    {
        actor = "manager-a", token = "manager-a-demo-token", attack = "claims-reconcile",
        claims = new Dictionary<string, string>
        {
            ["reason"] = "Quarterly cycle count discrepancy",
            ["change_ticket"] = "CHG-4821"
        }
    }),
};

foreach (var item in cases)
{
    var response = await CallTool(item.Item2, item.Item3);
    var status = Classify(item.Item1, response);
    Console.WriteLine($"[{status}] {item.Item1}");
    Console.WriteLine($"  {response}");
}

string Classify(string name, string response)
{
    var lower = response.ToLowerInvariant();

    // Positive controls: a legitimate call must succeed cleanly.
    if (name is "capabilities" or "authorized operator write" or "claims: valid reconcile evidence")
        return lower.Contains("error") || lower.Contains("exception") ? "FAIL" : "PASS";

    // Conditional-access probes: success means a predicate came back, not a
    // flat allow/deny. "claims: warehouse scoping narrows predicate" and
    // "claims: unknown claim key ignored" both expect the request to
    // proceed normally (no spoofing rejection) and still be conditional.
    if (name is "cross-tenant read" or "claims: warehouse scoping narrows predicate"
        or "claims: unknown claim key ignored")
        return lower.Contains("conditional") && !lower.Contains("\"iserror\":true") ? "PASS" : "FAIL";

    return lower.Contains("denied") || lower.Contains("unauthorized") || lower.Contains("invalid actor") ||
           lower.Contains("iserror") || lower.Contains("\"allowed\":false")
        ? "PASS"
        : "FAIL";
}

async Task<string> CallTool(string name, object arguments)
{
    var payload = JsonSerializer.Serialize(new
    {
        jsonrpc = "2.0",
        id = Guid.NewGuid().ToString("N"),
        method = "tools/call",
        @params = new { name, arguments }
    });

    using var request = new HttpRequestMessage(HttpMethod.Post, url)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json")
    };
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
    request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");

    using var response = await client.SendAsync(request);
    return await response.Content.ReadAsStringAsync();
}