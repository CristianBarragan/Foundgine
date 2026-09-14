using Foundgine.Runtime;
using Foundgine.Core.Execution;
using Foundgine.Providers.Tools.MCP;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpSecurityIntegrationTests
{
    [Fact]
    public void Mutation_approval_uses_host_subject_not_agent_supplied_approver()
    {
        var context = CreateSecurityContext("host-subject");
        var mutations = new RecordingMutations();
        var tools = new FoundgineMcpMutationTools(mutations, () => context);

        var json = "{\"operations\":[{\"entity\":1,\"kind\":\"Create\",\"fields\":{}}]}";
        _ = tools.Approve(json, "attacker-controlled-name");

        Assert.Equal("host-subject", mutations.LastApprovedBy);
        Assert.Equal("host-subject", mutations.LastRequest?.Security?.Subject);
    }

    [Fact]
    public void Mutation_approval_requires_host_security_context()
    {
        var tools = new FoundgineMcpMutationTools(new RecordingMutations());
        var json = "{\"operations\":[{\"entity\":1,\"kind\":\"Create\",\"fields\":{}}]}";

        Assert.Throws<UnauthorizedAccessException>(() => tools.Approve(json, "attacker"));
    }

    private static SecurityExecutionContext CreateSecurityContext(string subject) =>
        new(
            new SecurityWarrant(
                "warrant-1", "issuer", subject, "mcp", [],
                SecurityWarrantConstraints.Unrestricted, DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5), "nonce-1", "key-1", null, []),
            subject,
            "mcp",
            "tenant-1",
            null);

    private sealed class RecordingMutations : IFoundgineMutations
    {
        public string? LastApprovedBy { get; private set; }
        public SemanticMutationRequest? LastRequest { get; private set; }

        public Task<MutationExecutionResult> ExecuteAsync(
            SemanticMutationRequest request,
            ExecutionContext? context = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public MutationDryRunResult DryRun(SemanticMutationRequest request)
        {
            LastRequest = request;
            return new MutationDryRunResult("fingerprint", [], []);
        }

        public MutationPlanApproval Approve(SemanticMutationRequest request, string approvedBy)
        {
            LastRequest = request;
            LastApprovedBy = approvedBy;
            return new MutationPlanApproval(request, "approval", "fingerprint", approvedBy, DateTimeOffset.UtcNow);
        }

        public Task<MutationExecutionResult> ExecuteApprovedAsync(
            MutationPlanApproval approval,
            ExecutionContext? context = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
