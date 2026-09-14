using System.Text.Json;
using Foundgine.Providers.Tools.MCP;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpCapabilityDiscoverySecurityTests
{
    [Fact]
    public void Discovery_requires_host_security_context()
    {
        var tools = new FoundgineMcpTools(new StubFoundgine(), securityContextFactory: () => null);

        Assert.Throws<UnauthorizedAccessException>(() => tools.DescribeCapabilities());
    }

    [Fact]
    public void Discovery_does_not_accept_agent_supplied_security_context()
    {
        var context = CreateContext("host-subject", "tenant-1");
        var tools = new FoundgineMcpTools(new StubFoundgine(), securityContextFactory: () => context);

        // The MCP tool has no parameter through which an agent can replace the host context.
        var json = tools.DescribeCapabilities();
        Assert.Contains("orders.read", json, StringComparison.Ordinal);
        Assert.DoesNotContain("customers.read", json, StringComparison.Ordinal);
    }

    private static SecurityExecutionContext CreateContext(string subject, string tenant) =>
        new(
            new SecurityWarrant(
                "warrant-discovery", "issuer", subject, "mcp",
                [new CapabilityGrant("orders.read", "read", [])],
                SecurityWarrantConstraints.Unrestricted,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5),
                "nonce-discovery", "key-1", null, []),
            subject, "mcp", tenant);

    private sealed class StubFoundgine : Foundgine.Runtime.IFoundgine
    {
        public Foundgine.Core.Semantic.Authorization.SemanticAuthorizationCapabilities DescribeCapabilities() =>
            throw new NotImplementedException();

        public Foundgine.Core.Semantic.Capabilities.SemanticCapabilityContract DescribeCapabilityContract() =>
            Contract();

        public Foundgine.Core.Semantic.Capabilities.SemanticCapabilityContract DescribeCapabilityContract(
            SecurityExecutionContext security) =>
            Contract() with
            {
                Capabilities = Contract().Capabilities
                    .Where(c => SecurityWarrantAuthorization.Allows(security.Warrant, security.Subject,
                        security.Audience, c.Id, c.Operation, security.Tenant, security.ResourceScope))
                    .ToArray()
            };

        public Foundgine.Core.Semantic.SemanticVersionSet DescribeVersionSet() => throw new NotImplementedException();

        public Foundgine.Runtime.DryRunResult DryRun(Foundgine.Core.Semantic.SemanticRequest request) =>
            throw new NotImplementedException();

        public Foundgine.Runtime.PlanApproval ApprovePlan(Foundgine.Core.Semantic.SemanticRequest request,
            string approvedBy) => throw new NotImplementedException();

        public Task<Foundgine.Core.Execution.ExecutionResult> ExecuteApprovedAsync(
            Foundgine.Runtime.PlanApproval approval, Foundgine.Core.Execution.ExecutionContext? context = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<Foundgine.Core.Execution.ExecutionResult> ExecuteAsync(
            Foundgine.Core.Semantic.SemanticRequest request, Foundgine.Core.Execution.ExecutionContext? context = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<Foundgine.Core.Execution.ExecutionResult> ExecuteAsync(
            Foundgine.Core.Semantic.Intent.ReadIntent intent, Foundgine.Core.Execution.ExecutionContext? context = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        private static Foundgine.Core.Semantic.Capabilities.SemanticCapabilityContract Contract() =>
            new(1,
            [
                new("orders.read", "Read Orders", new Foundgine.Core.Abstractions.EntityId(1),
                        Foundgine.Core.Abstractions.AuthorizationDecision.Allowed, [], [], [], [], [])
                    { Operation = "read" },
                new("customers.read", "Read Customers", new Foundgine.Core.Abstractions.EntityId(2),
                        Foundgine.Core.Abstractions.AuthorizationDecision.Allowed, [], [], [], [], [])
                    { Operation = "read" }
            ]);
    }
}