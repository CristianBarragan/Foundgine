using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Security;
using Xunit;

namespace Foundgine.Security.Tests.Penetration;

/// <summary>Attacks the exact plan/IR binding used as the final execution rail.</summary>
public sealed class SecurityProofRailsPenetrationTests
{
    [Fact]
    public void Proof_cannot_be_reused_for_a_different_plan_instance()
    {
        var ir = CreateIr(SecurityInvariantIds.AuthorizationRequired);
        var compiler = new GoodCompiler();
        var certified = SecurityInvariantProofGate.AttachAndValidate(new TestPlan("p1"), ir, compiler);
        var cloned = certified with { SecurityProof = certified.SecurityProof };

        Assert.NotSame(certified, cloned);
        Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantExecutionGate.EnsureExecutable(cloned, ir));
    }

    [Fact]
    public void Proof_cannot_be_reused_after_provider_substitution()
    {
        var ir = CreateIr(SecurityInvariantIds.AuthorizationRequired);
        var compiler = new GoodCompiler();
        var certified = SecurityInvariantProofGate.AttachAndValidate(new TestPlan("p1"), ir, compiler);
        var substituted = certified with { Provider = "attacker-provider" };

        Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantExecutionGate.EnsureExecutable(substituted, ir));
    }

    [Fact]
    public void Proof_cannot_be_reused_after_security_obligation_change()
    {
        var original = CreateIr(SecurityInvariantIds.AuthorizationRequired);
        var modified = CreateIr(SecurityInvariantIds.TenantIsolation);
        var certified = SecurityInvariantProofGate.AttachAndValidate(new TestPlan("p1"), original, new GoodCompiler());

        Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantExecutionGate.EnsureExecutable(certified, modified));
    }

    [Fact]
    public void Provider_declaration_without_concrete_security_evaluation_cannot_certify_critical_invariants()
    {
        var ir = CreateIr(SecurityInvariantIds.RuntimeAuthorization);

        Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantProofGate.AttachAndValidate(new TestPlan("weak"), ir, new DeclarationOnlyCompiler()));
    }

    [Fact]
    public void Unknown_invariant_cannot_cross_the_certification_boundary()
    {
        var ir = CreateIr("security.attacker-controlled");

        Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantProofGate.AttachAndValidate(new TestPlan("p1"), ir, new GoodCompiler()));
    }

    [Fact]
    public void Empty_security_obligations_cannot_produce_an_executable_plan()
    {
        var ir = CreateIr();

        Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantProofGate.AttachAndValidate(new TestPlan("p1"), ir, new GoodCompiler()));
    }

    private static ExecutionIR CreateIr(params string[] invariants) =>
        Foundgine.Testing.ExecutionIRTestFactory.Create(
            new ExecutionIRNode(
                1,
                ExecutionOperation.Scan,
                new EntityId(1),
                [new FieldId(1)],
                null,
                null,
                []),
            invariants);

    private sealed record TestPlan(string Provider) : ProviderPlan(Provider);

    private sealed class DeclarationOnlyCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants =>
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id).ToArray();

        public ProviderPlan Compile(ExecutionIR ir) => new TestPlan("weak");
    }

    private sealed class GoodCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
        IProviderSecurityConformanceEvaluator
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants =>
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id).ToArray();

        public ProviderPlan Compile(ExecutionIR ir) => new TestPlan("p1");

        public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan) =>
            new(plan.Provider, ir.RequiredSecurityInvariants, ir.RequiredSecurityInvariants, []);
    }
}