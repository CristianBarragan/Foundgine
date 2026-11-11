using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Domain.Models;

namespace Foundgine.SupplyChain.Domain;

// Relationship -> physical foreign key. One connection configures both directions of a pair, so
// SalesOrder.Shipments and Shipment.Order share a single connection. HasForeignKey selects the key on the
// target type (one-to-many); HasForeignKeyOnSource selects it on the source (many-to-one).

internal sealed class SupplyChainConnections : FoundgineConnectionConfiguration
{
    public override void Configure(FoundgineConnectionBuilder connections)
    {
        // Customer 1 - * SalesOrder
        connections.HasRelationship<Customer, SalesOrder>()
            .Using<Customer, SalesOrder>()
            .HasForeignKey(o => o.CustomerId)
            .References(c => c.Id);

        // SalesOrder 1 - * SalesOrderLine
        connections.HasRelationship<SalesOrder, SalesOrderLine>()
            .Using<SalesOrder, SalesOrderLine>()
            .HasForeignKey(l => l.OrderId)
            .References(o => o.Id);

        // SalesOrder 1 - * Shipment (also defines Shipment.Order)
        connections.HasRelationship<SalesOrder, Shipment>()
            .Using<SalesOrder, Shipment>()
            .HasForeignKey(s => s.OrderId)
            .References(o => o.Id);

        // SalesOrderLine * - 1 CatalogProduct
        connections.HasRelationship<SalesOrderLine, CatalogProduct>()
            .Using<SalesOrderLine, CatalogProduct>()
            .HasForeignKeyOnSource(l => l.ProductId)
            .References(p => p.Id);

        // CatalogProduct * - 1 Supplier
        connections.HasRelationship<CatalogProduct, Supplier>()
            .Using<CatalogProduct, Supplier>()
            .HasForeignKeyOnSource(p => p.SupplierId)
            .References(s => s.Id);

        // CatalogProduct * - 1 Category
        connections.HasRelationship<CatalogProduct, Category>()
            .Using<CatalogProduct, Category>()
            .HasForeignKeyOnSource(p => p.CategoryId)
            .References(c => c.Id);

        // CatalogProduct 1 - * InventoryPosition
        connections.HasRelationship<CatalogProduct, InventoryPosition>()
            .Using<CatalogProduct, InventoryPosition>()
            .HasForeignKey(i => i.ProductId)
            .References(p => p.Id);

        // InventoryPosition * - 1 Warehouse
        connections.HasRelationship<InventoryPosition, Warehouse>()
            .Using<InventoryPosition, Warehouse>()
            .HasForeignKeyOnSource(i => i.WarehouseId)
            .References(w => w.Id);

        // Shipment * - 1 Carrier
        connections.HasRelationship<Shipment, Carrier>()
            .Using<Shipment, Carrier>()
            .HasForeignKeyOnSource(s => s.CarrierId)
            .References(c => c.Id);

        // Shipment * - 1 Warehouse
        connections.HasRelationship<Shipment, Warehouse>()
            .Using<Shipment, Warehouse>()
            .HasForeignKeyOnSource(s => s.WarehouseId)
            .References(w => w.Id);
    }
}
