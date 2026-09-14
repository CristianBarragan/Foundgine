using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Query;
using Foundgine.SupplyChain.Advanced.Semantics;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests;

public sealed class OpenIntentSupplyChainTests
{
    [Fact]
    public void Product_shipments_is_an_open_logical_traversal_over_the_real_supply_chain_path()
    {
        var model = SupplyChainSemanticModel.Build();
        var request = new ReadIntent(
            "Product",
            [
                new ReadSelection(
                    Relationship: "shipments",
                    Children: [new ReadSelection(Field: "Status")])
            ]);

        var semanticRequest = new ReadIntentCompiler(model).Compile(request);
        var graph = new Foundgine.Core.Semantic.Resolution.SemanticRequestResolver(model.Freeze().CreateSnapshot())
            .Resolve(semanticRequest);

        Assert.Equal(4, graph.Nodes.Count);
        Assert.Equal("Product", model.Get(graph.Nodes[0].EntityId).Name);
        Assert.Equal("PurchaseOrderLine", model.Get(graph.Nodes[1].EntityId).Name);
        Assert.Equal("PurchaseOrder", model.Get(graph.Nodes[2].EntityId).Name);
        Assert.Equal("Shipment", model.Get(graph.Nodes[3].EntityId).Name);
        var status = model.Get(graph.Nodes[3].EntityId).Fields.Single(x => x.Name == "Status");
        Assert.Contains(status.Id, graph.Nodes[3].Fields);
    }

    [Fact]
    public void Open_supply_chain_mutation_covers_generated_identity_value_flow_and_branching()
    {
        var model = SupplyChainSemanticModel.Build();
        var graph = new SemanticMutationIntentBuilder(model)
            .Create("PurchaseOrder", "order")
            .Set("SupplierId", 1)
            .Set("WarehouseId", 1)
            .Set("Status", "Open")
            .Return("Id")
            .Create("PurchaseOrderLine", "line")
            .SetFrom("PurchaseOrderId", "order", "Id")
            .Set("ProductId", 1)
            .Set("Quantity", 25m)
            .Return("Id", "PurchaseOrderId")
            .Create("Shipment", "shipment")
            .SetFrom("PurchaseOrderId", "order", "Id")
            .Set("ExpectedArrival", new DateTime(2026, 9, 5))
            .Set("Status", "Planned")
            .Set("Quantity", 25m)
            .Return("Id", "PurchaseOrderId")
            .Update("PurchaseOrder")
            .Set("Status", "Open")
            .Where("Id", SemanticFilterOperator.Eq, 1)
            .Return("Id")
            .Build();

        var plan = new SemanticMutationPlanner().Plan(graph);

        Assert.Equal(4, plan.Operations.Count);
        Assert.Equal(2, plan.Dependencies.Count);
        var linePurchaseOrderId =
            model.ResolveEntity("PurchaseOrderLine").Fields.Single(x => x.Name == "PurchaseOrderId").Id;
        var shipmentPurchaseOrderId =
            model.ResolveEntity("Shipment").Fields.Single(x => x.Name == "PurchaseOrderId").Id;
        Assert.Contains(plan.Dependencies, x => x.ToOperationId == "1" && x.TargetField == linePurchaseOrderId);
        Assert.Contains(plan.Dependencies, x => x.ToOperationId == "2" && x.TargetField == shipmentPurchaseOrderId);
        Assert.IsType<SemanticFieldFilter>(plan.Operations[3].Filter);
    }
}