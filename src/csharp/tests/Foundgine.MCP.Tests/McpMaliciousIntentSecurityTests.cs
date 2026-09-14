using Foundgine.Core.Serialization;
using Foundgine.Providers.Tools.MCP;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

/// <summary>
/// hostile-agent corpus. These tests deliberately treat MCP/AI payloads
/// as attacker-controlled data and assert that security authority cannot be
/// manufactured inside the intent document.
/// </summary>
public sealed class McpMaliciousIntentSecurityTests
{
    [Theory]
    [InlineData("tenantId")]
    [InlineData("subject")]
    [InlineData("audience")]
    [InlineData("warrant")]
    [InlineData("authorization")]
    [InlineData("capabilityId")]
    [InlineData("provider")]
    [InlineData("connectionString")]
    [InlineData("sql")]
    public void Agent_cannot_manufacture_security_or_provider_authority(string forbiddenProperty)
    {
        var adapter = new JsonReadIntentAdapter();
        var json =
            $"{{\"rootEntity\":\"Customer\",\"selections\":[{{\"field\":\"Id\"}}],\"{forbiddenProperty}\":\"attacker-controlled\"}}";

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));

        Assert.Contains("Invalid JSON read intent", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(forbiddenProperty, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Agent_cannot_switch_tenant_by_using_a_hostile_filter_value_as_security_context()
    {
        var adapter = new JsonReadIntentAdapter();
        var intent = adapter.Parse("""
                                   {
                                   "rootEntity": "Customer",
                                   "selections": [{ "field": "Id" }],
                                   "filter": {
                                   "kind": "field",
                                   "field": "TenantId",
                                   "operator": "Eq",
                                   "value": "victim-tenant"
                                   }
                                   }
                                   """);

        // The filter is merely untrusted semantic input. It does not populate
        // SecurityExecutionContext and therefore cannot replace the host tenant.
        Assert.Null(intent.Security);
        Assert.Equal("Customer", intent.RootEntity);
    }

    [Fact]
    public void Agent_cannot_compose_an_undeclared_capability_into_the_intent()
    {
        var adapter = new JsonReadIntentAdapter();
        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Parse("""
            {
            "rootEntity": "Customer",
            "selections": [{ "field": "Id" }],
            "capabilityId": "Customer.admin"
            }
            """));

        Assert.Contains("capabilityId", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Malicious_predicate_structure_is_bounded_before_planning()
    {
        var adapter = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions
        {
            MaxFilterDepth = 4,
            MaxFilterNodes = 8
        });

        var json = """
                   {
                   "rootEntity": "Customer",
                   "selections": [{ "field": "Id" }],
                   "filter": {
                   "kind": "or",
                   "expressions": [
                   { "kind": "or", "expressions": [
                   { "kind": "or", "expressions": [
                   { "kind": "or", "expressions": [
                   { "kind": "field", "field": "Id", "operator": "Eq", "value": 1 }
                   ] }
                   ] }
                   ] }
                   ]
                   }
                   }
                   """;

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));
        Assert.Contains("filter", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Host_security_context_is_the_only_authority_seen_by_mcp_execution()
    {
        var host = CreateContext("host-subject", "tenant-host");
        var recording = new RecordingFoundgine();
        var tools = new FoundgineMcpTools(
            recording,
            securityContextFactory: () => host);

        var json = """
                   {
                   "rootEntity": "Customer",
                   "selections": [{ "field": "Id" }],
                   "tenantId": "attacker-tenant"
                   }
                   """;

        Assert.Throws<InvalidOperationException>(() => tools.ExecuteQueryAsync(json).GetAwaiter().GetResult());
        Assert.Null(recording.ReceivedSecurity);
    }

    private static SecurityExecutionContext CreateContext(string subject, string tenant) =>
        new(
            new SecurityWarrant(
                "warrant-m52", "issuer", subject, "mcp", [],
                SecurityWarrantConstraints.Unrestricted,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5),
                "nonce-m52", "key-1", null, []),
            subject, "mcp", tenant);

    private sealed class RecordingFoundgine : Foundgine.Runtime.IFoundgine
    {
        public SecurityExecutionContext? ReceivedSecurity { get; private set; }

        public Foundgine.Core.Semantic.Authorization.SemanticAuthorizationCapabilities DescribeCapabilities() =>
            throw new NotImplementedException();

        public Foundgine.Core.Semantic.Capabilities.SemanticCapabilityContract DescribeCapabilityContract() =>
            throw new NotImplementedException();

        public Foundgine.Core.Semantic.Capabilities.SemanticCapabilityContract
            DescribeCapabilityContract(SecurityExecutionContext security) => throw new NotImplementedException();

        public Foundgine.Core.Semantic.SemanticVersionSet DescribeVersionSet() => throw new NotImplementedException();

        public Foundgine.Runtime.DryRunResult DryRun(Foundgine.Core.Semantic.SemanticRequest request) =>
            throw new NotImplementedException();

        public Foundgine.Runtime.PlanApproval ApprovePlan(Foundgine.Core.Semantic.SemanticRequest request,
            string approvedBy) => throw new NotImplementedException();

        public Task<Foundgine.Core.Execution.ExecutionResult> ExecuteApprovedAsync(
            Foundgine.Runtime.PlanApproval approval, Foundgine.Core.Execution.ExecutionContext? context = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<Foundgine.Core.Execution.ExecutionResult> ExecuteAsync(
            Foundgine.Core.Semantic.Intent.ReadIntent intent, Foundgine.Core.Execution.ExecutionContext? context = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedSecurity = intent.Security;
            throw new InvalidOperationException("Hostile intent reached semantic execution.");
        }

        public Task<Foundgine.Core.Execution.ExecutionResult> ExecuteAsync(
            Foundgine.Core.Semantic.SemanticRequest request, Foundgine.Core.Execution.ExecutionContext? context = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}