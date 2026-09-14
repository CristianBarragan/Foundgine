using Foundgine.Core.Abstractions;
using Foundgine.Generated;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic;

namespace Foundgine.SupplyChain.Application;

/// <summary>
/// Application-specific semantic enrichment for the Supply Chain sample.
/// Structural entities, fields, identities and direct relationships are
/// discovered from Foundgine.Core.Semantic.Metadata. This class only adds meaning that
/// infrastructure metadata cannot infer, such as the logical
/// Customer.transactions traversal.
/// </summary>
public static class SupplyChainSemanticConfiguration
{
    public static MetadataRegistry Metadata { get; } = GeneratedMetadata.Registry;

    public static SemanticModel Model { get; } = Build();

    public static EntityId Customer => GeneratedSemanticModel.Customer.Entity;
    public static EntityId SalesOrder => GeneratedSemanticModel.SalesOrder.Entity;
    public static EntityId SalesOrderLine => GeneratedSemanticModel.SalesOrderLine.Entity;
    public static EntityId CatalogProduct => GeneratedSemanticModel.CatalogProduct.Entity;
    public static EntityId Supplier => GeneratedSemanticModel.Supplier.Entity;
    public static EntityId Category => GeneratedSemanticModel.Category.Entity;
    public static EntityId InventoryPosition => GeneratedSemanticModel.InventoryPosition.Entity;
    public static EntityId Warehouse => GeneratedSemanticModel.Warehouse.Entity;
    public static EntityId Shipment => GeneratedSemanticModel.Shipment.Entity;
    public static EntityId Carrier => GeneratedSemanticModel.Carrier.Entity;

    public static readonly RelationshipId CustomerOrders = Relationship("CustomerERP", "Orders");
    public static readonly RelationshipId OrderLines = Relationship("SalesOrderERP", "Lines");
    public static readonly RelationshipId LineProduct = Relationship("SalesOrderLineERP", "Product");
    public static readonly RelationshipId ProductSupplier = Relationship("CatalogProductERP", "Supplier");
    public static readonly RelationshipId ProductCategory = Relationship("CatalogProductERP", "Category");
    public static readonly RelationshipId ProductInventory = Relationship("CatalogProductERP", "InventoryPositions");
    public static readonly RelationshipId InventoryWarehouse = Relationship("InventoryPositionERP", "Warehouse");
    public static readonly RelationshipId OrderShipments = Relationship("SalesOrderERP", "Shipments");
    public static readonly RelationshipId ShipmentCarrier = Relationship("ShipmentERP", "Carrier");
    public static readonly RelationshipId ShipmentWarehouse = Relationship("ShipmentERP", "Warehouse");
    public static readonly RelationshipId ShipmentOrder = Relationship("ShipmentERP", "Order");

    /// <summary>
    /// Business meaning that cannot be inferred from storage metadata:
    /// Customer.transactions is a logical traversal over
    /// Customer → CustomerRelationship → Contract → Transaction.
    /// The showcase configuration adds it only when those entities and
    /// relationships are present; the expanded graph remains visible to
    /// authorization and planning.
    /// </summary>
    private static SemanticModel Build()
    {
        var builder = Metadata.FromMetadata();

        // The base SupplyChain sample currently exposes the logical path
        // through its semantic showcase model when those relationships exist.
        // Keep the configuration conservative: no invented capability is
        // created if the structural metadata does not contain the path.
        if (TryBuildCustomerTransactions(builder, out var model))
            return model;

        return builder.Build();
    }

    private static bool TryBuildCustomerTransactions(
        SemanticModelBuilder builder,
        out SemanticModel model)
    {
        model = null!;

        var customer = FindEntity("CustomerERP");
        var relationship = Metadata.Relationships.FirstOrDefault(x =>
            x.Source == customer &&
            string.Equals(x.Name, "CustomerRelationships", StringComparison.OrdinalIgnoreCase));

        if (relationship is null)
            return false;

        var relationshipEntity = relationship.Target;
        var contract = Metadata.Relationships.FirstOrDefault(x =>
            x.Source == relationshipEntity &&
            string.Equals(x.Name, "Contract", StringComparison.OrdinalIgnoreCase));

        if (contract is null)
            return false;

        var transactions = Metadata.Relationships.FirstOrDefault(x =>
            x.Source == contract.Target &&
            string.Equals(x.Name, "Transactions", StringComparison.OrdinalIgnoreCase));

        if (transactions is null)
            return false;

        builder.Traversal(customer, "transactions",
            relationship.Id,
            contract.Id,
            transactions.Id);

        model = builder.Build();
        return true;
    }

    private static EntityId FindEntity(string name) =>
        Metadata.Entities.Single(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).EntityId;

    private static RelationshipId Relationship(string entityName, string relationshipName) =>
        Metadata.Relationships
            .Single(x =>
                x.Name == relationshipName &&
                Metadata.GetEntity(x.Source).Name == entityName)
            .Id;
}
