using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Providers.AgentFramework.Tools;
using Foundgine.Runtime;

namespace Foundgine.AgentFramework.Tests;

internal static class Support
{
    public const string Subject = "alice@example.com";

    public const string MutationJson =
        """{"operations":[{"entity":1,"kind":"Create","fields":{"2":"Ada"},"returnFields":[2]}]}""";

    public static SecurityExecutionContext Context(string subject = Subject) =>
        new(
            new SecurityWarrant(
                "warrant-maf", "issuer", subject, "maf",
                [new CapabilityGrant("orders.write", "write", [])],
                SecurityWarrantConstraints.Unrestricted,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5),
                "nonce-maf", "key-1", null, []),
            subject, "maf", "tenant-a");

    public sealed class FixedProvider(SecurityExecutionContext? context) : ISecurityExecutionContextProvider
    {
        public SecurityExecutionContext? GetSecurityExecutionContext() => context;
    }

    /// <summary>Records every call so tests can prove nothing ran (or ran exactly once).</summary>
    public sealed class RecordingMutations(string fingerprint = "fp-1") : IFoundgineMutations
    {
        public int DryRuns, Approvals, Executions;
        public string? ApprovedBy;
        public SecurityExecutionContext? SecurityOnExecute;

        public MutationDryRunResult DryRun(SemanticMutationRequest request)
        {
            DryRuns++;
            return new MutationDryRunResult(fingerprint, [], ["create Customer"]);
        }

        public Task<MutationExecutionResult> ExecuteAsync(SemanticMutationRequest request,
            Foundgine.Core.Execution.ExecutionContext? context = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The adapter must never use the un-approved execute path.");

        public MutationPlanApproval Approve(SemanticMutationRequest request, string approvedBy)
        {
            Approvals++;
            ApprovedBy = approvedBy;
            return new MutationPlanApproval(request, "approval-1", fingerprint, approvedBy, DateTimeOffset.UtcNow);
        }

        public Task<MutationExecutionResult> ExecuteApprovedAsync(MutationPlanApproval approval,
            Foundgine.Core.Execution.ExecutionContext? context = null, CancellationToken cancellationToken = default)
        {
            Executions++;
            SecurityOnExecute = approval.Request.Security;
            return Task.FromResult(new MutationExecutionResult(
                new MutationBatchResult([]), fingerprint, "result-fp", approval.ApprovalId, approval.ApprovedBy));
        }
    }

    public sealed class StubFoundgine : IFoundgine
    {
        public Foundgine.Core.Semantic.Authorization.SemanticAuthorizationCapabilities DescribeCapabilities() =>
            throw new NotImplementedException();
        public Foundgine.Core.Semantic.Capabilities.SemanticCapabilityContract DescribeCapabilityContract() =>
            throw new NotImplementedException();
        public Foundgine.Core.Semantic.Capabilities.SemanticCapabilityContract DescribeCapabilityContract(
            SecurityExecutionContext security) => new(1, []);
        public Foundgine.Core.Semantic.SemanticVersionSet DescribeVersionSet() => throw new NotImplementedException();
        public DryRunResult DryRun(Foundgine.Core.Semantic.SemanticRequest request) => throw new NotImplementedException();
        public PlanApproval ApprovePlan(Foundgine.Core.Semantic.SemanticRequest request, string approvedBy) =>
            throw new NotImplementedException();
        public Task<Foundgine.Core.Execution.ExecutionResult> ExecuteApprovedAsync(PlanApproval approval,
            Foundgine.Core.Execution.ExecutionContext? context = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<Foundgine.Core.Execution.ExecutionResult> ExecuteAsync(Foundgine.Core.Semantic.SemanticRequest request,
            Foundgine.Core.Execution.ExecutionContext? context = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<Foundgine.Core.Execution.ExecutionResult> ExecuteAsync(Foundgine.Core.Semantic.Intent.ReadIntent intent,
            Foundgine.Core.Execution.ExecutionContext? context = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    public static FoundgineAgentTools CreateTools(
        SecurityExecutionContext? security, IFoundgineMutations? mutations, bool openIntent = false)
    {
        var provider = new FixedProvider(security);
        var reads = new Foundgine.Providers.Tools.MCP.FoundgineMcpTools(
            new StubFoundgine(), securityContextProvider: provider);
        return new FoundgineAgentTools(reads, provider, mutations, openIntent);
    }
}
