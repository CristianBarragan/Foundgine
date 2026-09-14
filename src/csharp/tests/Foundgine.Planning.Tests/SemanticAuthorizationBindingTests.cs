using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.IR;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class SemanticAuthorizationBindingTests
{
    [Fact]
    public void Authorization_evidence_is_bound_to_the_contract_fingerprint()
    {
        var contract = CreateContract("Customer");
        var otherContract = CreateContract("Client");
        var operation = CreateOperation();

        var result = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy())
            .AuthorizeWithEvidence(contract, operation);

        Assert.Equal(contract.ContractFingerprint, result.Evidence.ContractFingerprint);
        Assert.NotEqual(contract.ContractFingerprint, otherContract.ContractFingerprint);

        var exception = Assert.Throws<SemanticAuthorizationException>(() =>
            result.EnsureMatches(otherContract));

        Assert.Contains("bound to semantic contract", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Authorization_fingerprint_changes_when_the_contract_changes()
    {
        var operation = CreateOperation();
        var first = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy())
            .AuthorizeWithEvidence(CreateContract("Customer"), operation);
        var second = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy())
            .AuthorizeWithEvidence(CreateContract("Client"), operation);

        Assert.NotEqual(first.Evidence.AuthorizationFingerprint, second.Evidence.AuthorizationFingerprint);
    }

    [Fact]
    public void Compatibility_authorize_path_uses_the_bound_authorization_boundary()
    {
        var contract = CreateContract("Customer");
        var operation = CreateOperation();

        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy())
            .Authorize(contract, operation);

        Assert.Equal(operation.Root.EntityId, authorized.Root.EntityId);
    }

    private static SemanticOperation CreateOperation()
    {
        var graph = new SemanticGraph();
        graph.AddRoot(new EntityId(1), [new FieldId(1)]);
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
