using Foundgine.Providers.Tools.MCP;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpSecurityExecutionContextProviderTests
{
    private static SecurityExecutionContext CreateContext(string subject, string tenant) =>
        new(
            new SecurityWarrant(
                "warrant-provider", "issuer", subject, "mcp",
                [new CapabilityGrant("orders.read", "read", [])],
                SecurityWarrantConstraints.Unrestricted,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5),
                "nonce-provider", "key-1", null, []),
            subject, "mcp", tenant);

    private sealed class FixedProvider(SecurityExecutionContext? context) : ISecurityExecutionContextProvider
    {
        public SecurityExecutionContext? GetSecurityExecutionContext() => context;
    }

    private sealed class StubFoundgine : Foundgine.Runtime.IFoundgine
    {
        public Foundgine.Core.Semantic.Authorization.SemanticAuthorizationCapabilities DescribeCapabilities() =>
            throw new NotImplementedException();

        public Foundgine.Core.Semantic.Capabilities.SemanticCapabilityContract DescribeCapabilityContract() =>
            throw new NotImplementedException();

        public Foundgine.Core.Semantic.Capabilities.SemanticCapabilityContract DescribeCapabilityContract(
            SecurityExecutionContext security) =>
            new(1, []);

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
    }

    [Fact]
    public void FoundgineMcpTools_accepts_a_securityContextProvider_directly()
    {
        var context = CreateContext("host-subject", "tenant-1");
        var tools = new FoundgineMcpTools(new StubFoundgine(), securityContextProvider: new FixedProvider(context));

        var json = tools.DescribeCapabilities();

        Assert.NotNull(json);
    }

    [Fact]
    public void FoundgineMcpTools_throws_when_provider_supplies_no_context()
    {
        var tools = new FoundgineMcpTools(new StubFoundgine(), securityContextProvider: new FixedProvider(null));

        Assert.Throws<UnauthorizedAccessException>(() => tools.DescribeCapabilities());
    }

    [Fact]
    public void FoundgineMcpTools_rejects_both_securityContextProvider_and_securityContextFactory()
    {
        var context = CreateContext("host-subject", "tenant-1");

        Assert.Throws<ArgumentException>(() => new FoundgineMcpTools(
            new StubFoundgine(),
            securityContextProvider: new FixedProvider(context),
            securityContextFactory: () => context));
    }

    [Fact]
    public void FoundgineMcpTools_defaults_to_no_security_context_when_neither_supplied()
    {
        var tools = new FoundgineMcpTools(new StubFoundgine());

        Assert.Throws<UnauthorizedAccessException>(() => tools.DescribeCapabilities());
    }

    [Fact]
    public void FoundgineMcpMutationTools_accepts_a_securityContextProvider_directly()
    {
        var context = CreateContext("host-subject", "tenant-1");
        var tools = new FoundgineMcpMutationTools(
            mutations: null,
            securityContextFactory: null,
            securityContextProvider: new FixedProvider(context));

        // No mutations engine configured, so this should fail on that, not on security.
        var ex = Assert.Throws<InvalidOperationException>(() => tools.DryRun("""{"operations":[]}"""));
        Assert.Contains("not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FoundgineMcpMutationTools_rejects_both_securityContextProvider_and_securityContextFactory()
    {
        var context = CreateContext("host-subject", "tenant-1");

        Assert.Throws<ArgumentException>(() => new FoundgineMcpMutationTools(
            mutations: null,
            securityContextFactory: () => context,
            securityContextProvider: new FixedProvider(context)));
    }

    [Fact]
    public void FoundgineMcpMutationTools_positional_securityContextFactory_still_works()
    {
        var context = CreateContext("host-subject", "tenant-1");

        // Existing hosts call this constructor positionally: (mutations, securityContextFactory).
        // The new securityContextProvider parameter must not shift this slot.
        var tools = new FoundgineMcpMutationTools(null, () => context);

        var ex = Assert.Throws<InvalidOperationException>(() => tools.DryRun("""{"operations":[]}"""));
        Assert.Contains("not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}