using Xunit;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Resolution;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticContractRuntimeBoundaryTests
{
    [Fact]
    public void Resolver_accepts_the_runtime_contract_snapshot()
    {
        var entityId = EntityId.Create("Customer");
        var fieldId = FieldId.Create("Customer", "Id");
        var model = new SemanticModelBuilder()
            .Entity(entityId, "Customer", entity => entity
                .Field(fieldId, "Id", typeof(int), capabilities: SemanticFieldCapabilities.Selectable)
                .Identity(fieldId, "Id"))
            .Build()
            .Freeze();

        var contract = model.CreateSnapshot();
        var request = new SemanticRequest(
            entityId,
            [new SemanticSelection(fieldId, null, [])]);

        var graph = new SemanticRequestResolver(contract).Resolve(request);

        Assert.Single(graph.Nodes);
        Assert.Equal(contract.ContractFingerprint, model.ContractFingerprint);
    }

    [Fact]
    public void Resolver_model_constructor_is_only_a_compatibility_bridge()
    {
        var entityId = EntityId.Create("Customer");
        var fieldId = FieldId.Create("Customer", "Id");
        var model = new SemanticModelBuilder()
            .Entity(entityId, "Customer", entity => entity
                .Field(fieldId, "Id", typeof(int), capabilities: SemanticFieldCapabilities.Selectable)
                .Identity(fieldId, "Id"))
            .Build();

        var customer = model.ResolveEntity("Customer");
        var request = new SemanticRequest(
            customer.Id,
            [new SemanticSelection(customer.Identity.FieldId, null, [])]);

#pragma warning disable CS0618
        var graph = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
#pragma warning restore CS0618

        Assert.Single(graph.Nodes);
    }
}