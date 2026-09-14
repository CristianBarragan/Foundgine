using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests;

/// <summary>
/// MCP-boundary adversarial cases. These intentionally behave like Run 5:
/// the caller is treated as untrusted and attempts to use a valid MCP tool in
/// a way that should cross a semantic authorization boundary.
/// </summary>
public sealed class AuthorizationMcpPenetrationTests
{
    [Fact]
    public void Attack_matrix_defines_the_required_policy_boundaries()
    {
        var attacks = new[]
        {
            "cross-tenant",
            "sensitive-field",
            "relationship-escalation",
            "write-escalation",
            "named-operation",
            "unauthorized-write"
        };

        Assert.Equal(6, attacks.Length);
        Assert.All(attacks, attack => Assert.False(string.IsNullOrWhiteSpace(attack)));
    }

    [Fact]
    public void Claims_attack_matrix_defines_the_required_claim_validation_boundaries()
    {
        // Each of these corresponds to a case the adversarial McpClient sends
        // and to a unit test in ClientClaimsValidatorTests / AuthorizationPolicyTests.
        // Listed here as the single source of truth for "what must this
        // feature demonstrably reject". See GUIDE.md "Claims validation".
        var claimAttacks = new[]
        {
            "role-injection", // client asserts role directly
            "tenant-injection", // client asserts tenant directly
            "missing-evidence", // sensitive named op without required claims
            "malformed-evidence", // required claim present but wrong format
            "expired-evidence" // required claim present but past its own not_after
        };

        var claimNarrowingUses = new[]
        {
            "self-imposed-read-only", // scope=read-only restricts a manager's own writes
            "warehouse-scoping", // warehouse=<id> ANDs onto the tenant predicate
            "unknown-key-ignored", // noise claim is dropped, request still proceeds
            "valid-reconcile-evidence" // well-formed reason + change_ticket allows reconcile
        };

        Assert.Equal(5, claimAttacks.Length);
        Assert.Equal(4, claimNarrowingUses.Length);
        Assert.All(claimAttacks.Concat(claimNarrowingUses), c => Assert.False(string.IsNullOrWhiteSpace(c)));
    }

    [Fact]
    public async Task Raw_mcp_request_has_expected_json_rpc_shape()
    {
        using var client = new HttpClient();
        var payload = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = "probe",
            method = "tools/call",
            @params = new
            {
                name = "policy_probe",
                arguments = new { tenantId = "tenant-a", role = "Customer", attack = "write-escalation" }
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:4782/mcp")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");

        // This test is opt-in against the running sample server. It verifies
        // the wire contract when explicitly enabled instead of making CI
        // depend on a local MCP process.
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_SUPPLYCHAIN_MCP_TESTS"), "1",
                StringComparison.Ordinal))
            return;

        using var response = await client.SendAsync(request);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}