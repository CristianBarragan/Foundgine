using Foundgine.Generated;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic;

namespace Foundgine.SupplyChain.Advanced.Semantics;

/// <summary>
/// Application semantic configuration for the Supply Chain showcase.
/// Structural entities, fields, identities and direct relationships come from
/// generated Foundgine metadata. A small strongly typed manual semantic overlay
/// then enriches Product and ProductComponent, while this layer adds the
/// application-level logical traversals that metadata cannot infer.
/// </summary>
public static class SupplyChainSemanticModel
{
    public static IMetadataCatalog Metadata { get; } = GeneratedMetadata.Build();
    public static SemanticModel Model { get; } = Build();

    public static EntityId Product => Entity("Product");
    public static EntityId Component => Entity("ProductComponent");
    public static EntityId Supplier => Entity("Supplier");
    public static EntityId Shipment => Entity("Shipment");
    public static EntityId InventoryLot => Entity("InventoryLot");
    public static EntityId Warehouse => Entity("Warehouse");
    public static EntityId BusinessUnit => Entity("BusinessUnit");
    public static EntityId CustomerOrder => Entity("CustomerOrder");
    public static EntityId CustomerOrderLine => Entity("CustomerOrderLine");
    public static EntityId PurchaseOrder => Entity("PurchaseOrder");
    public static EntityId PurchaseOrderLine => Entity("PurchaseOrderLine");
    public static EntityId Certification => Entity("SupplierCertification");
    public static EntityId ComplianceIncident => Entity("ComplianceIncident");

    public static SemanticModel Build() =>
        Metadata.FromMetadata()
            .Overlay(ManualSupplyChainSemanticModel.Model)
            .Traversal("Product", "shipments", "purchaseOrderLines", "purchaseOrder", "shipments")
            .Traversal("Product", "supplierIncidents", "purchaseOrderLines", "purchaseOrder", "supplier", "incidents")
            .Build();

    public static FieldId Field(string entityName, string fieldName) =>
        Model.Get(Entity(entityName)).Fields.Single(field =>
            string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase)).Id;

    public static RelationshipId Relationship(string entityName, string relationshipName) =>
        Model.Get(Entity(entityName)).Relationships.Single(relationship =>
            string.Equals(relationship.Name, relationshipName, StringComparison.OrdinalIgnoreCase)).Id;

    private static EntityId Entity(string entityName) =>
        Metadata.Entities.Single(entity =>
            string.Equals(entity.Name, entityName, StringComparison.OrdinalIgnoreCase)).EntityId;
}