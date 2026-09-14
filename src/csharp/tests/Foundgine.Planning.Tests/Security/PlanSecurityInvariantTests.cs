using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Security;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests.Security;

public sealed class PlanSecurityInvariantTests
{
    [Fact]
    public void Plan_requirements_are_derived_from_authorized_shape()
    {
        var node = new SemanticPlanNode(
            1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(2)], null, null, []);

        var plan = SecurityInvariantPlanRequirements.Attach(new SemanticPlan(node));

        Assert.NotNull(plan.RequiredSecurityInvariants);
        Assert.Contains(SecurityInvariantIds.AuthorizationRequired, plan.RequiredSecurityInvariants);
        Assert.Contains(SecurityInvariantIds.ParameterizedValues, plan.RequiredSecurityInvariants);
        Assert.Contains(SecurityInvariantIds.PlanCacheContextIsolation, plan.RequiredSecurityInvariants);
        Assert.Contains(SecurityInvariantIds.FieldVisibility, plan.RequiredSecurityInvariants);
    }

    [Fact]
    public void Authorization_predicate_requires_runtime_authorization()
    {
        var predicate = new AuthorizationPredicate(
            AuthorizationPredicateKind.Equal,
            Left: new AuthorizationPredicate(AuthorizationPredicateKind.Constant, Value: "1"),
            Right: new AuthorizationPredicate(AuthorizationPredicateKind.Constant, Value: "1"));

        var node = new SemanticPlanNode(
            1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(2)], null, null, [], null, predicate);

        var plan = SecurityInvariantPlanRequirements.Attach(new SemanticPlan(node));

        Assert.NotNull(plan.RequiredSecurityInvariants);
        Assert.Contains(SecurityInvariantIds.RuntimeAuthorization, plan.RequiredSecurityInvariants);
    }

    [Fact]
    public void Provider_proof_fails_closed_when_an_invariant_is_not_preserved()
    {
        var node = new SemanticPlanNode(
            1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(2)], null, null, []);
        var plan = SecurityInvariantPlanRequirements.Attach(new SemanticPlan(node)) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var ir = ExecutionIRCompiler.Compile(plan);
        var compiler = new MissingInvariantCompiler();
        var providerPlan = compiler.Compile(ir);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantProofGate.AttachAndValidate(providerPlan, ir, compiler));

        Assert.Contains(SecurityInvariantIds.ParameterizedValues, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Provider_proof_is_attached_when_all_required_invariants_are_preserved()
    {
        var node = new SemanticPlanNode(
            1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(2)], null, null, []);
        var plan = SecurityInvariantPlanRequirements.Attach(new SemanticPlan(node)) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var ir = ExecutionIRCompiler.Compile(plan);
        var compiler = new FullInvariantCompiler();
        var providerPlan = compiler.Compile(ir);

        var proved = SecurityInvariantProofGate.AttachAndValidate(providerPlan, ir, compiler);

        Assert.NotNull(proved.SecurityProof);
        Assert.True(proved.SecurityProof!.IsSatisfied);
        Assert.Equal(ir.RequiredSecurityInvariants.OrderBy(x => x), proved.SecurityProof.Required);
    }

    [Fact]
    public void Security_requirements_participate_in_plan_fingerprint()
    {
        var node = new SemanticPlanNode(
            1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(2)], null, null, []);

        var baseline = new SemanticPlan(node, [SecurityInvariantIds.ParameterizedValues]);
        var stronger = new SemanticPlan(node,
            [SecurityInvariantIds.ParameterizedValues, SecurityInvariantIds.TenantIsolation]);

        Assert.NotEqual(
            SemanticPlanFingerprint.CreateShapeKey(baseline),
            SemanticPlanFingerprint.CreateShapeKey(stronger));
    }

    private sealed class MissingInvariantCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants => [SecurityInvariantIds.AuthorizationRequired];

        public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan) =>
            new(
                plan.Provider,
                ir.RequiredSecurityInvariants,
                ir.RequiredSecurityInvariants.Where(PreservedSecurityInvariants.Contains).ToArray(),
                Array.Empty<string>());

        public ProviderPlan Compile(ExecutionIR ir) => new TestPlan();
    }

    private sealed class FullInvariantCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
        IProviderSecurityConformanceEvaluator
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants =>
        [
            SecurityInvariantIds.AuthorizationRequired,
            SecurityInvariantIds.FieldVisibility,
            SecurityInvariantIds.ParameterizedValues,
            SecurityInvariantIds.PlanCacheContextIsolation
        ];

        public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan) =>
            new(
                plan.Provider,
                ir.RequiredSecurityInvariants,
                ir.RequiredSecurityInvariants.Where(PreservedSecurityInvariants.Contains).ToArray(),
                Array.Empty<string>());

        public ProviderPlan Compile(ExecutionIR ir) => new TestPlan();
    }

    private sealed record TestPlan() : ProviderPlan("test");
}