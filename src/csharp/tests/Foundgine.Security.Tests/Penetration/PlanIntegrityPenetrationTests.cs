using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Security;
using Foundgine.Testing;
using Xunit;

namespace Foundgine.Security.Tests.Penetration;

/// <summary>Attempts to transplant, forge, replay, or mutate execution security evidence.</summary>
public sealed class PlanIntegrityPenetrationTests
{
    [Fact]
    public void Missing_security_proof_cannot_execute()
    {
        var ir = CreateIr(SecurityInvariantIds.AuthorizationRequired);
        var plan = new TestProviderPlan("pentest");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantExecutionGate.EnsureExecutable(plan, ir));

        Assert.Contains("security proof", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Security_proof_for_one_plan_cannot_be_attached_to_another_plan()
    {
        var ir = CreateIr(SecurityInvariantIds.AuthorizationRequired);
        var compiler = new GoodCompiler();
        var first = SecurityInvariantProofGate.AttachAndValidate(new TestProviderPlan("pentest"), ir, compiler);
        var second = first with { Provider = "pentest" };

        Assert.NotSame(first, second);
        Assert.Throws<InvalidOperationException>(() => SecurityInvariantExecutionGate.EnsureExecutable(second, ir));
    }

    [Fact]
    public void Security_proof_for_one_execution_ir_cannot_authorize_a_modified_ir()
    {
        var ir = CreateIr(SecurityInvariantIds.AuthorizationRequired);
        var certified =
            SecurityInvariantProofGate.AttachAndValidate(new TestProviderPlan("pentest"), ir, new GoodCompiler());
        var modified = CreateIr(SecurityInvariantIds.TenantIsolation);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantExecutionGate.EnsureExecutable(certified, modified));

        Assert.Contains("bound", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Provider_must_concretely_evaluate_security_critical_invariants()
    {
        var ir = CreateIr(SecurityInvariantIds.TenantIsolation);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantProofGate.AttachAndValidate(new TestProviderPlan("weak"), ir,
                new DeclarationOnlyCompiler()));

        Assert.Contains("conformance evaluator", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_security_invariant_cannot_be_smuggled_into_execution_ir()
    {
        var ir = CreateIr("pentest.unknown-invariant");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantProofGate.AttachAndValidate(new TestProviderPlan("pentest"), ir, new GoodCompiler()));

        Assert.Contains("Unknown required security invariant", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ExecutionIR CreateIr(string invariant) => ExecutionIRTestFactory.Create(
        new ExecutionIRNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, []),
        [invariant]);

    private sealed record TestProviderPlan(string Provider) : ProviderPlan(Provider);

    private sealed class DeclarationOnlyCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants => [SecurityInvariantIds.TenantIsolation];
        public ProviderPlan Compile(ExecutionIR ir) => new TestProviderPlan("weak");
    }

    private sealed class GoodCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
        IProviderSecurityConformanceEvaluator
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants =>
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id).ToArray();

        public ProviderPlan Compile(ExecutionIR ir) => new TestProviderPlan("pentest");

        public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan) =>
            new(plan.Provider, ir.RequiredSecurityInvariants, ir.RequiredSecurityInvariants, []);
    }
}