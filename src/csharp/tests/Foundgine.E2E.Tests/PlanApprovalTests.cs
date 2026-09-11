using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Security;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Runtime;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

public sealed class PlanApprovalTests
{
    [Fact]
    public async Task Approval_executes_when_current_plan_matches_approved_plan()
    {
        var engine = CreateEngine(out var provider);
        var request = Request();

        var approval = engine.ApprovePlan(request, "human@example");
        var result = await engine.ExecuteApprovedAsync(approval);

        Assert.Equal(1, provider.ExecutionCount);
        Assert.NotNull(result);
        Assert.Equal(approval.PlanFingerprint, result.Evidence?.PlanFingerprint);
        Assert.NotNull(result.Receipt);
        Assert.Equal(approval.ApprovalId, result.Receipt!.ApprovalId);
        Assert.Equal("human@example", result.Receipt.ApprovedBy);
        Assert.Equal(approval.PlanFingerprint, result.Receipt.PlanFingerprint);
        Assert.False(string.IsNullOrWhiteSpace(result.Receipt.ResultFingerprint));
    }

    [Fact]
    public async Task Approval_rejects_when_semantic_version_changes()
    {
        var engine = CreateEngine(out var provider);
        var approval = engine.ApprovePlan(Request(), "human@example");
        var tampered = approval with { SemanticModelVersion = "sha256:changed" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ExecuteApprovedAsync(tampered));
        Assert.Equal(0, provider.ExecutionCount);
    }

    [Fact]
    public async Task Approval_rejects_when_plan_fingerprint_changes()
    {
        var engine = CreateEngine(out var provider);
        var approval = engine.ApprovePlan(Request(), "human@example");
        var tampered = approval with { PlanFingerprint = approval.PlanFingerprint + "-tampered" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ExecuteApprovedAsync(tampered));
        Assert.Equal(0, provider.ExecutionCount);
    }

    private static FoundgineEngine CreateEngine(out TestExecutionProvider provider)
    {
        provider = new TestExecutionProvider();
        return new FoundgineEngine(
            Banking.BankingSemanticModel.Build(),
            new AllowAllSemanticAuthorizationPolicy(),
            new Planner(),
            new TestProviderPlanCompiler(),
            provider);
    }

    private static SemanticRequest Request() => new(
        Banking.BankingSemanticModel.Customer,
        [new SemanticSelection(new FieldId(2), null, [])]);

    private sealed class TestProviderPlanCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
        IProviderSecurityConformanceEvaluator
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants =>
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id).ToArray();

        public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan) =>
            new(
                plan.Provider,
                ir.RequiredSecurityInvariants,
                ir.RequiredSecurityInvariants.Where(PreservedSecurityInvariants.Contains).ToArray(),
                Array.Empty<string>());

        public ProviderPlan Compile(ExecutionIR ir) => new TestPlan();
    }

    private sealed class TestExecutionProvider : IExecutionProvider
    {
        public int ExecutionCount { get; private set; }

        public Task<ExecutionResult> ExecuteAsync(
            ProviderPlan plan,
            ExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(new ExecutionResult(
                [new ExecutionRow(new Dictionary<string, object?> { ["Name"] = "Alice" })],
                Evidence: ExecutionEvidenceFactory.Create(
                    "test",
                    "placeholder",
                    [1],
                    1,
                    0)));
        }
    }

    private sealed record TestPlan() : ProviderPlan("test");
}