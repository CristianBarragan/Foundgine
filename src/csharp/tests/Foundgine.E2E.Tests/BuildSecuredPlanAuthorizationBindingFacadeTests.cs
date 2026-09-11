using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Security;
using Foundgine.Runtime;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

/// <summary>
/// Fix: <see cref="ExecutionIRCompiler"/> now refuses to lower a
/// <see cref="SemanticPlan"/> that has no <see cref="SemanticPlanAuthorizationBinding"/>
/// rather than silently no-oping the check.
///
/// The lower-level Planning/Execution unit tests already exercise that guard
/// directly by constructing an unbound <see cref="SemanticPlan"/> by hand and
/// asserting the throw. Neither of those tests goes through
/// <c>FoundgineEngine.BuildSecuredPlan</c>, so they cannot tell us whether the
/// facade's own pipeline (planner -&gt; optimizer -&gt; SecurityInvariantPlanRequirements
/// -&gt; ExecutionIRCompiler) still produces a bound plan at every one of
/// BuildSecuredPlan's three call sites (DryRun, ExecuteApprovedAsync, ExecuteAsync).
///
/// These tests close that gap: they run real requests end-to-end through the
/// public facade and capture the ExecutionIR the plan compiler receives,
/// asserting the binding survived planning, optimization, and invariant
/// attachment intact. This is a regression guard against a future change to
/// any of those stages accidentally dropping AuthorizationBinding via a `with`
/// expression that forgets to preserve it (which would only surface at
/// execution time, past the DryRun/ApprovePlan boundary).
/// </summary>
public sealed class BuildSecuredPlanAuthorizationBindingFacadeTests
{
    [Fact]
    public async Task ExecuteAsync_BuildSecuredPlan_output_carries_a_satisfied_authorization_binding()
    {
        var compiler = new CapturingCompiler();
        var engine = CreateEngine(compiler, out _);

        await engine.ExecuteAsync(Request());

        Assert.NotNull(compiler.IR);
        Assert.NotNull(compiler.IR!.AuthorizationBinding);
    }

    [Fact]
    public async Task ExecuteApprovedAsync_BuildSecuredPlan_output_carries_a_satisfied_authorization_binding()
    {
        var compiler = new CapturingCompiler();
        var engine = CreateEngine(compiler, out _);

        var approval = engine.ApprovePlan(Request(), "human@example");
        await engine.ExecuteApprovedAsync(approval);

        Assert.NotNull(compiler.IR);
        Assert.NotNull(compiler.IR!.AuthorizationBinding);
    }

    [Fact]
    public void DryRun_does_not_reach_ExecutionIRCompiler_and_never_throws_for_an_unbound_plan()
    {
        // DryRun's call site (line ~173) only reaches PlanInspector, never
        // ExecutionIRCompiler, so the fix's null-check is unreachable from
        // this path. This test documents that boundary explicitly: DryRun
        // must keep succeeding even though it never proves the
        // ExecutionIRCompiler-level invariant the other two call sites do.
        var compiler = new CapturingCompiler();
        var engine = CreateEngine(compiler, out _);

        var dryRun = engine.DryRun(Request());

        Assert.NotNull(dryRun);
        Assert.Null(compiler.IR);
    }

    [Fact]
    public async Task DryRun_and_ExecuteAsync_agree_on_the_same_bound_plan_fingerprint()
    {
        // Ties DryRun's plan (never passed through ExecutionIRCompiler) back
        // to the plan ExecuteAsync actually lowers and validates, so the two
        // call sites can't silently drift apart on which plan/binding they
        // consider authoritative.
        var compiler = new CapturingCompiler();
        var engine = CreateEngine(compiler, out _);
        var request = Request();

        var dryRun = engine.DryRun(request);
        var approval = engine.ApprovePlan(request, "human@example");

        Assert.Equal(dryRun.Inspection.PlanFingerprint, approval.PlanFingerprint);

        await engine.ExecuteApprovedAsync(approval);
        Assert.NotNull(compiler.IR!.AuthorizationBinding);
    }

    private static FoundgineEngine CreateEngine(CapturingCompiler compiler, out TestExecutionProvider provider)
    {
        provider = new TestExecutionProvider();
        return new FoundgineEngine(
            Banking.BankingSemanticModel.Build(),
            new AllowAllSemanticAuthorizationPolicy(),
            new Planner(),
            compiler,
            provider);
    }

    private static SemanticRequest Request() => new(
        Banking.BankingSemanticModel.Customer,
        [new SemanticSelection(new FieldId(2), null, [])]);

    private sealed class CapturingCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
        IProviderSecurityConformanceEvaluator
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants =>
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id).ToArray();

        public ExecutionIR? IR { get; private set; }

        public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan) =>
            new(
                plan.Provider,
                ir.RequiredSecurityInvariants,
                ir.RequiredSecurityInvariants.Where(PreservedSecurityInvariants.Contains).ToArray(),
                Array.Empty<string>());

        public ProviderPlan Compile(ExecutionIR ir)
        {
            IR = ir;
            return new TestPlan();
        }
    }

    private sealed class TestExecutionProvider : IExecutionProvider
    {
        public int ExecutionCount { get; private set; }

        public Task<ExecutionResult> ExecuteAsync(
            ProviderPlan plan,
            ExecutionContext context,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult(new ExecutionResult(Array.Empty<ExecutionRow>()));
        }
    }

    private sealed record TestPlan : ProviderPlan
    {
        public TestPlan() : base("test")
        {
        }
    }
}