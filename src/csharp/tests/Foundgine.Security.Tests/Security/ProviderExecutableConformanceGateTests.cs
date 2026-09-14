using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Security;
using Xunit;

namespace Foundgine.Security.Tests.Security;

public sealed class ProviderExecutableConformanceGateTests
{
    [Fact]
    public void Gate_rejects_executable_conformance_violation()
    {
        var ir = TestIr(SecurityInvariantIds.AuthorizationRequired);
        var compiler = new HostileCertifiedCompiler();
        var plan = compiler.Compile(ir);

        Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantProofGate.AttachAndValidate(plan, ir, compiler));
    }

    private static ExecutionIR TestIr(string invariant) =>
        Foundgine.Testing.ExecutionIRTestFactory.Create(
            new ExecutionIRNode(
                1,
                default,
                default,
                [],
                null,
                null,
                [],
                null,
                null),
            [invariant]);

    private sealed class HostileCertifiedCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
        IProviderSecurityConformanceEvaluator
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants =>
            [SecurityInvariantIds.AuthorizationRequired];

        public ProviderPlan Compile(ExecutionIR ir) => new TestPlan();

        public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan) =>
            new(
                plan.Provider,
                ir.RequiredSecurityInvariants,
                [],
                ["compiled provider plan lost authorization predicate"]);
    }

    private sealed record TestPlan() : ProviderPlan("hostile-certified");

    [Fact]
    public void Provider_profile_alone_cannot_cross_security_critical_boundary()
    {
        var ir = TestIr(SecurityInvariantIds.AuthorizationRequired);
        var compiler = new ProfileOnlyCompiler();
        var plan = compiler.Compile(ir);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantProofGate.AttachAndValidate(plan, ir, compiler));

        Assert.Contains("no concrete security conformance evaluator", exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Certificate_is_bound_to_the_exact_returned_plan_instance()
    {
        var ir = TestIr(SecurityInvariantIds.AuthorizationRequired);
        var compiler = new HonestCompiler();
        var certified = SecurityInvariantProofGate.AttachAndValidate(compiler.Compile(ir), ir, compiler);

        var transplanted = certified with { };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantExecutionGate.EnsureExecutable(transplanted, ir));

        Assert.Contains("exact provider plan", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Certificate_cannot_be_replayed_against_a_different_execution_ir()
    {
        var ir = TestIr(SecurityInvariantIds.AuthorizationRequired);
        var compiler = new HonestCompiler();
        var certified = SecurityInvariantProofGate.AttachAndValidate(compiler.Compile(ir), ir, compiler);
        var differentIr = TestIr(SecurityInvariantIds.TenantIsolation);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantExecutionGate.EnsureExecutable(certified, differentIr));

        Assert.Contains("exact provider plan and Execution IR", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Provider_identity_mismatch_is_rejected_even_when_certificate_is_satisfied()
    {
        var ir = TestIr(SecurityInvariantIds.AuthorizationRequired);
        var compiler = new HonestCompiler();
        var certified = SecurityInvariantProofGate.AttachAndValidate(compiler.Compile(ir), ir, compiler);
        var mismatched = new DifferentProviderPlan { SecurityProof = certified.SecurityProof };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantExecutionGate.EnsureExecutable(mismatched, ir));

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ProfileOnlyCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants => [SecurityInvariantIds.AuthorizationRequired];
        public ProviderPlan Compile(ExecutionIR ir) => new TestPlan();
    }

    private sealed class HonestCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
        IProviderSecurityConformanceEvaluator
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants => [SecurityInvariantIds.AuthorizationRequired];
        public ProviderPlan Compile(ExecutionIR ir) => new TestPlan();

        public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan) =>
            new(plan.Provider, ir.RequiredSecurityInvariants, ir.RequiredSecurityInvariants, []);
    }

    private sealed record DifferentProviderPlan() : ProviderPlan("different-provider");
}