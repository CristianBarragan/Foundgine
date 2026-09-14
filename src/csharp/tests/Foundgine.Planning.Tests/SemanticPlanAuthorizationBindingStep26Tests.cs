using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.IR;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class SemanticPlanAuthorizationBindingStep26Tests
{
    [Fact]
    public void Planner_binds_plan_to_authorization_evidence()
    {
        var contract = CreateContract("Customer");
        var operation = CreateOperation();
        var authorization = Authorize(contract, operation);

        var plan = new Planner().Plan(contract, authorization);

        Assert.NotNull(plan.AuthorizationBinding);
        Assert.Equal(contract.ContractFingerprint, plan.AuthorizationBinding!.ContractFingerprint);
        Assert.Equal(authorization.Evidence.AuthorizationFingerprint,
            plan.AuthorizationBinding.AuthorizationFingerprint);
    }

    [Fact]
    public void Planner_rejects_authorization_evidence_from_another_contract()
    {
        var contract = CreateContract("Customer");
        var otherContract = CreateContract("Client");
        var operation = CreateOperation();
        var authorization = Authorize(otherContract, operation);

        Assert.Throws<SemanticAuthorizationException>(() =>
            new Planner().Plan(contract, authorization));
    }

    [Fact]
    public void Authorization_binding_rejects_a_different_authorization_decision()
    {
        var contract = CreateContract("Customer");
        var operation = CreateOperation();
        var first = Authorize(contract, operation);
        var second = new SemanticAuthorizationResult(
            first.Operation,
            SemanticAuthorizationEvidence.Create(contract, CreateOperationWithDifferentField()));

        var plan = new Planner().Plan(contract, first);

        Assert.Throws<InvalidOperationException>(() =>
            plan.AuthorizationBinding!.EnsureMatches(contract, second.Evidence));
    }

    [Fact]
    public void Rewrite_preserves_authorization_binding()
    {
        var contract = CreateContract("Customer");
        var operation = CreateOperation();
        var authorization = Authorize(contract, operation);
        var plan = new Planner().Plan(contract, authorization);

        var rewritten = plan with { Root = plan.Root };

        Assert.Equal(plan.AuthorizationBinding, rewritten.AuthorizationBinding);
    }

    private static SemanticAuthorizationResult Authorize(
        SemanticContractSnapshot contract,
        SemanticOperation operation) =>
        new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy())
            .AuthorizeWithEvidence(contract, operation);

    private static SemanticOperation CreateOperation()
    {
        var graph = new SemanticGraph();
        graph.AddRoot(new EntityId(1), [new FieldId(1)]);
        return SemanticOperationCompiler.Compile(graph);
    }

    private static SemanticOperation CreateOperationWithDifferentField()
    {
        var graph = new SemanticGraph();
        graph.AddRoot(new EntityId(1), [new FieldId(10)]);
        return SemanticOperationCompiler.Compile(graph);
    }

    private static SemanticContractSnapshot CreateContract(string name)
    {
#pragma warning disable CS0618
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), name, entity => entity
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(10), "Name", typeof(string)))
            .Build();
#pragma warning restore CS0618

        return model.Freeze().CreateSnapshot();
    }
}