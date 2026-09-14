using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Query;
using Foundgine.SupplyChain.Advanced.Semantics;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests;

/// <summary>
/// Supply Chain mutation cases deliberately exercise the open mutation surface
/// beyond CRUD: branching generated-value flow, target filters, upsert conflicts,
/// relationship effects and fail-closed authoring validation.
/// </summary>
public sealed class OpenIntentMutationSecurityTests
{
    [Fact]
    public void Purchase_order_fan_out_preserves_identity_flow_to_line_and_shipment()
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
            .Set("Quantity", 100m)
            .Return("Id", "PurchaseOrderId")
            .Create("Shipment", "shipment")
            .SetFrom("PurchaseOrderId", "order", "Id")
            .Set("ExpectedArrival", new DateTime(2026, 9, 5))
            .Set("Status", "Planned")
            .Set("Quantity", 100m)
            .Return("Id", "PurchaseOrderId")
            .Build();

        var plan = new SemanticMutationPlanner().Plan(graph);

        Assert.Equal(3, plan.Operations.Count);
        Assert.Equal(2, plan.Dependencies.Count);
        Assert.All(plan.Dependencies, dependency => Assert.Equal(0, int.Parse(dependency.FromOperationId)));
        var purchaseOrderLineId =
            model.ResolveEntity("PurchaseOrderLine").Fields.Single(x => x.Name == "PurchaseOrderId").Id;
        var shipmentPurchaseOrderId =
            model.ResolveEntity("Shipment").Fields.Single(x => x.Name == "PurchaseOrderId").Id;
        Assert.All(plan.Dependencies, dependency => Assert.Contains(
            dependency.TargetField, new[] { purchaseOrderLineId, shipmentPurchaseOrderId }));
    }

    [Fact]
    public void Update_and_delete_require_target_filters()
    {
        var model = SupplyChainSemanticModel.Build();

        var update = new SemanticMutationIntentBuilder(model).Update("PurchaseOrder").Set("Status", "Closed");
        var delete = new SemanticMutationIntentBuilder(model).Delete("PurchaseOrder");

        Assert.Throws<InvalidOperationException>(() => update.Build());
        Assert.Throws<InvalidOperationException>(() => delete.Build());
    }

    [Fact]
    public void Upsert_requires_explicit_conflict_semantics()
    {
        var model = SupplyChainSemanticModel.Build();
        var withoutConflict = new SemanticMutationIntentBuilder(model)
            .Upsert("PurchaseOrder")
            .Set("SupplierId", 1);

        Assert.Throws<InvalidOperationException>(() => withoutConflict.Build());

        var valid = new SemanticMutationIntentBuilder(model)
            .Upsert("PurchaseOrder")
            .Set("SupplierId", 1)
            .Set("WarehouseId", 1)
            .Set("Status", "Open")
            .Conflict("Id")
            .Return("Id")
            .Build();

        Assert.Single(valid.Operations);
        Assert.Single(valid.Operations[0].ConflictFields);
    }

    [Fact]
    public void Mutation_field_and_entity_names_are_resolved_before_planning()
    {
        var model = SupplyChainSemanticModel.Build();

        Assert.Throws<InvalidOperationException>(() =>
            new SemanticMutationIntentBuilder(model)
                .Create("PurchaseOrder")
                .Set("SupplirId", 1));

        Assert.Throws<InvalidOperationException>(() =>
            new SemanticMutationIntentBuilder(model)
                .Create("PurchseOrder"));
    }

    [Fact]
    public void Mutation_dependencies_cannot_reference_a_future_operation()
    {
        var model = SupplyChainSemanticModel.Build();
        var builder = new SemanticMutationIntentBuilder(model)
            .Create("PurchaseOrderLine", "line");

        // The builder deliberately rejects forward references rather than allowing
        // an execution planner/provider to invent an ordering later.
        Assert.Throws<InvalidOperationException>(() =>
            builder.SetFrom("PurchaseOrderId", "order", "Id"));
    }

    [Fact]
    public void Target_filters_are_part_of_the_semantic_mutation_not_provider_text()
    {
        var model = SupplyChainSemanticModel.Build();
        var graph = new SemanticMutationIntentBuilder(model)
            .Update("PurchaseOrder")
            .Set("Status", "Closed")
            .Where("Id", SemanticFilterOperator.Eq, 42)
            .Return("Id")
            .Build();

        var operation = Assert.Single(graph.Operations);
        var filter = Assert.IsType<SemanticFieldFilter>(operation.Filter);
        Assert.Equal(SemanticFilterOperator.Eq, filter.Operator);
        Assert.Equal(42, filter.Value);
    }
}