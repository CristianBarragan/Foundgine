using Foundgine.Generated;
using Foundgine.SupplyChain.Advanced.Semantics;
using Foundgine.SupplyChain.Advanced.Application;
using Foundgine.SupplyChain.Advanced.Authorization;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Capabilities;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests;

public sealed class SemanticModelTests
{
    [Fact]
    public void Model_contains_the_supply_chain_entities_and_key_relationships()
    {
        var model = SupplyChainSemanticModel.Build();

        Assert.Equal(17, model.Entities.Count);
        var product = model.Get(SupplyChainSemanticModel.Product);
        Assert.Equal("Product", product.Name);
        Assert.Contains(product.Relationships, r => r.Name == "components");

        var warehouse = model.Get(SupplyChainSemanticModel.Warehouse);
        Assert.Contains(warehouse.Relationships, r => r.Name == "inventory");
        Assert.Contains(warehouse.Relationships, r => r.Name == "businessUnit");

        var purchaseOrder = model.Get(SupplyChainSemanticModel.PurchaseOrder);
        Assert.Contains(purchaseOrder.Relationships, r => r.Name == "lines");
        Assert.Contains(purchaseOrder.Relationships, r => r.Name == "shipments");
    }

    [Fact]
    public void Manual_semantic_overlay_is_composed_into_the_complete_runtime_model()
    {
        var model = SupplyChainSemanticModel.Build();
        var product = model.Get(SupplyChainSemanticModel.Product);
        var component = model.Get(SupplyChainSemanticModel.Component);

        Assert.Equal(17, model.Entities.Count);
        Assert.Contains(product.EffectiveAliases, a => a.Name == "Item");
        Assert.Contains(product.EffectiveAliases, a => a.Name == "Item2");
        Assert.Contains(product.Fields.Single(x => x.Name == "Sku").EffectiveAliases, a => a.Name == "PartNumber");
        Assert.Contains(product.Fields.Single(x => x.Name == "Sku").EffectiveConstraints,
            c => c.Kind == SemanticConstraintKind.Pattern);
        Assert.True(product.Fields.Single(x => x.Name == "SafetyStock").Capabilities
            .HasFlag(SemanticFieldCapabilities.Writable));
        Assert.Contains(product.Relationships, r => r.Name == "components" && r.Target == component.Id);
        Assert.Contains(component.Relationships, r => r.Name == "componentProduct" && r.Target == product.Id);

        // Structural metadata remains the source of the canonical entity identity.
        Assert.Equal(GeneratedMetadata.Build().GetEntity(product.Id).EntityId, product.Id);
    }

    [Fact]
    public void Execution_limits_are_application_policy_not_generated_semantic_topology()
    {
        Assert.Equal(5, SupplyChainExecutionLimits.RecursiveBomMaxDepth);
        Assert.Equal(50, SupplyChainExecutionLimits.MaximumPageSize);
        Assert.Equal(10000, SupplyChainExecutionLimits.MaximumTraversalNodes);
    }
}

public sealed class MetadataBackedAuthoringTests
{
    [Fact]
    public void Semantic_model_is_discovered_from_generated_structural_metadata()
    {
        var model = SupplyChainSemanticModel.Build();

        Assert.Equal(17, model.Entities.Count);
        Assert.Equal(17, SupplyChainSemanticModel.Metadata.Entities.Count());
        Assert.Equal(15, SupplyChainSemanticModel.Metadata.Relationships.Count());

        Assert.Equal("Product", model.Get(SupplyChainSemanticModel.Product).Name);
        Assert.Equal("PurchaseOrder", model.Get(SupplyChainSemanticModel.PurchaseOrder).Name);
        Assert.Equal("Shipment", model.Get(SupplyChainSemanticModel.Shipment).Name);
    }

    [Fact]
    public void Logical_traversal_is_authored_by_names_and_expands_over_discovered_relationships()
    {
        var model = SupplyChainSemanticModel.Build();
        var traversal = model.GetTraversal(SupplyChainSemanticModel.Product, "shipments");

        Assert.Equal("shipments", traversal.Name);
        Assert.Equal(3, traversal.Path.Count);
        Assert.Equal("purchaseOrderLines",
            model.Get(SupplyChainSemanticModel.Product).Relationships.Single(x => x.Id == traversal.Path[0]).Name);
        Assert.Equal("purchaseOrder",
            model.Get(SupplyChainSemanticModel.PurchaseOrderLine).Relationships.Single(x => x.Id == traversal.Path[1])
                .Name);
        Assert.Equal("shipments",
            model.Get(SupplyChainSemanticModel.PurchaseOrder).Relationships.Single(x => x.Id == traversal.Path[2])
                .Name);
    }


    [Fact]
    public void Multi_hop_supplier_incident_traversal_is_hidden_when_an_intermediate_entity_is_denied()
    {
        var model = SupplyChainSemanticModel.Build();
        var customer = SupplyChainAuthorization.Create("tenant-a", SupplyChainRole.Customer);
        var manager = SupplyChainAuthorization.Create("tenant-a", SupplyChainRole.SupplyChainManager);

        var customerCapabilities = SemanticCapabilityContractDiscovery.Describe(model, customer);
        var managerCapabilities = SemanticCapabilityContractDiscovery.Describe(model, manager);

        Assert.DoesNotContain(customerCapabilities.Capabilities, x =>
            x.Id.Equals("Product.supplierIncidents.traverse", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(managerCapabilities.Capabilities, x =>
            x.Id.Equals("Product.supplierIncidents.traverse", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Authorization_resolves_fields_and_relationships_without_hard_coded_generated_ids()
    {
        Assert.Equal("RiskScore", SupplyChainSemanticModel.Model
            .Get(SupplyChainSemanticModel.Supplier).Fields
            .Single(x => x.Id == SupplyChainAuthorization.FieldIds.SupplierRiskScore).Name);

        Assert.Equal("incidents", SupplyChainSemanticModel.Model
            .Get(SupplyChainSemanticModel.Supplier).Relationships
            .Single(x => x.Id == SupplyChainAuthorization.RelationshipIds.SupplierIncidents).Name);
    }
}