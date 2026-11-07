using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Advanced.Domain;

namespace Foundgine.SupplyChain.Advanced.Semantics.Configuration;

// Realises each semantic relationship as a physical foreign key. One connection covers
// both directions of a pair. Product and ProductComponent are related three ways, so
// those connections name the navigation they configure.

public sealed class SupplyChainConnections : FoundgineConnectionConfiguration
{
    public override void Configure(FoundgineConnectionBuilder connections)
    {
        connections.HasRelationship<Warehouse, BusinessUnit>()
            .Using<Warehouse, BusinessUnit>()
            .HasForeignKeyOnSource(x => x.BusinessUnitId)
            .References(x => x.Id);

        connections.HasRelationship<Warehouse, InventoryLot>()
            .Using<Warehouse, InventoryLot>()
            .HasForeignKey(x => x.WarehouseId)
            .References(x => x.Id);

        connections.HasRelationship<Supplier, SupplierCertification>()
            .Using<Supplier, SupplierCertification>()
            .HasForeignKey(x => x.SupplierId)
            .References(x => x.Id);

        connections.HasRelationship<Supplier, ComplianceIncident>()
            .Using<Supplier, ComplianceIncident>()
            .HasForeignKey(x => x.SupplierId)
            .References(x => x.Id);

        connections.HasRelationship<Supplier, PurchaseOrder>()
            .Using<Supplier, PurchaseOrder>()
            .HasForeignKey(x => x.SupplierId)
            .References(x => x.Id);

        connections.HasRelationship<Product, ProductComponent>(x => x.Components)
            .Using<Product, ProductComponent>()
            .HasForeignKey(x => x.ParentProductId)
            .References(x => x.Id);

        connections.HasRelationship<Product, ProductComponent>(x => x.UsedInComponents)
            .Using<Product, ProductComponent>()
            .HasForeignKey(x => x.ComponentProductId)
            .References(x => x.Id);

        connections.HasRelationship<Product, PurchaseOrderLine>()
            .Using<Product, PurchaseOrderLine>()
            .HasForeignKey(x => x.ProductId)
            .References(x => x.Id);

        connections.HasRelationship<PurchaseOrder, PurchaseOrderLine>()
            .Using<PurchaseOrder, PurchaseOrderLine>()
            .HasForeignKey(x => x.PurchaseOrderId)
            .References(x => x.Id);

        connections.HasRelationship<PurchaseOrder, Shipment>()
            .Using<PurchaseOrder, Shipment>()
            .HasForeignKey(x => x.PurchaseOrderId)
            .References(x => x.Id);

        connections.HasRelationship<CustomerOrder, CustomerOrderLine>()
            .Using<CustomerOrder, CustomerOrderLine>()
            .HasForeignKey(x => x.CustomerOrderId)
            .References(x => x.Id);
    }
}
